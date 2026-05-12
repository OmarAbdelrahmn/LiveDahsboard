// Services/RiderShiftStatService.cs
using LiveDahsboard.Data;
using LiveDahsboard.DTOs;
using LiveDahsboard.Models;
using Microsoft.EntityFrameworkCore;

namespace LiveDahsboard.Services;

public class RiderShiftStatService(ApplicationDbContext db) : IRiderShiftStatService
{
    private static readonly TimeZoneInfo SaudiTz =
        TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");

    /// <summary>
    /// Shifts older than this are dropped as stale / un-reset API data.
    /// A realistic long shift is 10–11 h; 12 h gives a safe margin.
    /// </summary>
    private const int MaxShiftHours = 12;

    // ═══════════════════════════════════════════════════════════════════════
    // TIME HELPERS
    // ═══════════════════════════════════════════════════════════════════════

    private static DateTime NowUtc => DateTime.UtcNow;

    private static DateTime NowLocal =>
        TimeZoneInfo.ConvertTimeFromUtc(NowUtc, SaudiTz);

    private static DateOnly TodayLocal() =>
        DateOnly.FromDateTime(NowLocal);

    /// <summary>
    /// Hours elapsed since local midnight today — the absolute ceiling
    /// for any rider's working hours on today's date.
    /// e.g. at 11:41 AM → 11.68 h
    /// </summary>
    private static decimal ElapsedHoursSinceMidnightToday() =>
        (decimal)NowLocal.TimeOfDay.TotalHours;

    /// <summary>
    /// Strips sub-second ticks from a UTC DateTime so that the same
    /// calendar midnight always maps to EXACTLY the same bit pattern,
    /// regardless of when ConvertTimeToUtc is called.
    ///
    /// WHY THIS MATTERS FOR ORDERS:
    ///   midnightUtc is used as the DB key for Segment B (the today-portion
    ///   of a midnight-crossing shift).  If ConvertTimeToUtc returns even
    ///   1 tick differently across polls, every poll inserts a *new* row
    ///   instead of updating the existing one.  BuildStats then sums all
    ///   those duplicate rows → inflated order counts.
    /// </summary>
    private static DateTime CanonicalUtc(DateTime dt) =>
        new(dt.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond,
            DateTimeKind.Utc);

    // ═══════════════════════════════════════════════════════════════════════
    // HOURS CAP  (single authoritative guard, applied at both write and read)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns hours clamped to physically-possible bounds:
    ///   • never negative
    ///   • never > 24 h (absolute hard cap)
    ///   • for TODAY: never > elapsed hours since local midnight
    ///
    /// Applied at write-time in BuildSegments to stop bad values reaching
    /// the DB, AND at read-time in BuildStats to auto-correct any stale
    /// rows that were written before this fix existed.
    /// </summary>
    private static decimal CapHours(decimal hours, DateOnly date)
    {
        if (hours <= 0m) return 0m;
        hours = Math.Min(hours, 24m);

        if (date == TodayLocal())
            hours = Math.Min(hours, ElapsedHoursSinceMidnightToday());

        return hours;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UPSERT
    // ═══════════════════════════════════════════════════════════════════════
    public async Task UpsertBatchAsync(IEnumerable<RiderShiftStatIncoming> items)
    {
        var list = items
            .Where(i => i.ActiveShiftStartedAt.HasValue)
            .ToList();

        if (list.Count == 0) return;

        var allSegments = list.SelectMany(BuildSegments).ToList();
        if (allSegments.Count == 0) return;

        // ── Step 1: clean up orphaned today-records ────────────────────────
        //
        // PROBLEM THIS SOLVES:
        //   When a rider's ActiveShiftStartedAt changes between polls
        //   (shift reset, server restart, or midnight-key drift before this
        //   fix), the OLD DB row for today is no longer matched by the upsert
        //   key and is never updated.  It silently accumulates in the table.
        //   BuildStats previously summed ALL records for a rider on a date,
        //   so ghost rows inflated both orders and hours.
        //
        // FIX:
        //   For every rider+company that appears in TODAY's incoming segments,
        //   delete any existing DB record whose ActiveShiftStartedAt is NOT
        //   among the current active segment keys.  Those records are orphans
        //   that will never receive another update.
        //
        var today = TodayLocal();
        var todaySegments = allSegments.Where(s => s.Date == today).ToList();

        if (todaySegments.Count > 0)
        {
            // Build a lookup: (riderId, companyId) → set of ACTIVE shift keys
            var activeKeysByRider = todaySegments
                .GroupBy(s => (s.RiderId, s.CompanyId))
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(s => s.ActiveShiftStartedAt).ToHashSet());

            var todayRiderIds = activeKeysByRider.Keys.Select(k => k.RiderId).Distinct().ToList();
            var todayCompanyIds = activeKeysByRider.Keys.Select(k => k.CompanyId).Distinct().ToList();

            // Pull all today-records for these riders (small set — fast)
            var existingTodayRecords = await db.RiderShiftStats
                .Where(r => todayRiderIds.Contains(r.RiderId)
                         && todayCompanyIds.Contains(r.CompanyId)
                         && r.Date == today)
                .ToListAsync();

            // Records not covered by any active shift key are orphans
            var orphans = existingTodayRecords
                .Where(r => activeKeysByRider.TryGetValue(
                                (r.RiderId, r.CompanyId), out var activeKeys)
                            && !activeKeys.Contains(r.ActiveShiftStartedAt))
                .ToList();

            if (orphans.Count > 0)
                db.RiderShiftStats.RemoveRange(orphans);
        }

        // ── Step 2: standard upsert for all segments ───────────────────────
        var riderIds = allSegments.Select(s => s.RiderId).Distinct().ToList();
        var companyIds = allSegments.Select(s => s.CompanyId).Distinct().ToList();
        var shiftKeys = allSegments.Select(s => s.ActiveShiftStartedAt).Distinct().ToList();

        var existing = await db.RiderShiftStats
            .Where(r => riderIds.Contains(r.RiderId)
                     && companyIds.Contains(r.CompanyId)
                     && shiftKeys.Contains(r.ActiveShiftStartedAt))
            .ToListAsync();

        var map = existing.ToDictionary(
            r => (r.RiderId, r.CompanyId, r.ActiveShiftStartedAt));

        var now = NowUtc;

        foreach (var seg in allSegments)
        {
            // Cap hours at write-time — bad values should never reach the DB
            decimal safeHours = CapHours(seg.WorkingHours, seg.Date);

            var key = (seg.RiderId, seg.CompanyId, seg.ActiveShiftStartedAt);

            if (map.TryGetValue(key, out var record))
            {
                record.RiderName = seg.RiderName;
                record.Orders = seg.Orders;
                record.WorkingHours = safeHours;
                record.Wallet = seg.Wallet;
                record.LastUpdatedAt = now;
            }
            else
            {
                var newRecord = new RiderShiftStat
                {
                    RiderId = seg.RiderId,
                    RiderName = seg.RiderName,
                    CompanyId = seg.CompanyId,
                    ActiveShiftStartedAt = seg.ActiveShiftStartedAt,
                    Date = seg.Date,
                    Orders = seg.Orders,
                    WorkingHours = safeHours,
                    Wallet = seg.Wallet,
                    LastUpdatedAt = now,
                };
                db.RiderShiftStats.Add(newRecord);
                map[key] = newRecord;
            }
        }

        await db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // SEGMENT BUILDER
    // ═══════════════════════════════════════════════════════════════════════
    //
    //  Rules
    //  ─────
    //  1. Shifts older than MaxShiftHours → silently dropped (stale API data).
    //
    //  2. WorkedSeconds is clamped to actual elapsed wall-clock time so the
    //     API's carry-over seconds can never inflate hours.
    //
    //  3. Same-day shift → ONE segment.
    //     Hours = min(workedSeconds/3600, elapsedSinceShiftStart, elapsedSinceMidnight).
    //     The third term is the key guard that stops "14.9 h at 11:41 AM".
    //     Orders = raw API value (no split needed — whole shift is today).
    //
    //  4. Midnight-crossing shift → TWO segments.
    //     Segment A  date = shift-start date   key = shiftStartUtc (canonical)
    //     Segment B  date = today              key = midnightUtc   (canonical)
    //
    //     Hours are split at the local-midnight boundary, each side capped.
    //
    //     Orders are split proportionally by the CLAMPED hours (not raw API
    //     workedSeconds) so that inflated API values don't distort the ratio.
    //
    //     midnightUtc is passed through CanonicalUtc() to strip sub-second
    //     ticks → guarantees the same DB key on every poll → no duplicate
    //     midnight rows → no inflated order counts from summing duplicates.
    //
    private static IEnumerable<ShiftSegment> BuildSegments(RiderShiftStatIncoming item)
    {
        var shiftStartUtc = item.ActiveShiftStartedAt!.Value;
        var nowUtc = NowUtc;

        // ── Guard 1: drop shifts that are unrealistically old ──────────────
        double elapsedSinceStartD = (nowUtc - shiftStartUtc).TotalHours;
        if (elapsedSinceStartD > MaxShiftHours)
            yield break;

        // ── Guard 2: clamp WorkedSeconds to elapsed wall-clock time ───────
        decimal elapsedSinceStart = (decimal)elapsedSinceStartD;
        decimal totalHours = Math.Clamp(item.WorkedSeconds / 3600m, 0m, elapsedSinceStart);

        // Local times
        var shiftStartLocal = TimeZoneInfo.ConvertTimeFromUtc(shiftStartUtc, SaudiTz);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, SaudiTz);
        var shiftStartDate = DateOnly.FromDateTime(shiftStartLocal);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        // ── No midnight crossing ───────────────────────────────────────────
        if (shiftStartDate == todayLocal)
        {
            // Guard 3: hours cannot exceed time elapsed since local midnight.
            // Catches cases where the API sends WorkedSeconds from an unreset
            // previous shift counter that started earlier today.
            decimal elapsedSinceMidnight = (decimal)nowLocal.TimeOfDay.TotalHours;
            decimal safeHours = Math.Clamp(totalHours, 0m, elapsedSinceMidnight);

            yield return new ShiftSegment(
                Source: item,
                Date: shiftStartDate,
                ActiveShiftStartedAt: CanonicalUtc(shiftStartUtc),
                WorkingHours: safeHours,
                Orders: item.Orders); // whole shift is today

            yield break;
        }

        // ── Midnight crossing detected ─────────────────────────────────────
        var midnightLocal = shiftStartDate.ToDateTime(TimeOnly.MinValue).AddDays(1);

        // CanonicalUtc: strip sub-second ticks so this key is identical on
        // every poll → same DB row is updated, no duplicate midnight rows,
        // no inflated order sums.
        var midnightUtc = CanonicalUtc(
            TimeZoneInfo.ConvertTimeToUtc(midnightLocal, SaudiTz));

        // Hours before midnight (shift-start → midnight), capped to totalHours
        decimal hoursBeforeMidnight = Math.Clamp(
            (decimal)(midnightUtc - shiftStartUtc).TotalHours,
            0m, totalHours);

        // Hours after midnight (midnight → now), capped to elapsed today
        decimal elapsedTodayHours = (decimal)(nowUtc - midnightUtc).TotalHours;
        decimal hoursAfterMidnight = Math.Clamp(
            totalHours - hoursBeforeMidnight, 0m, elapsedTodayHours);

        // ── Proportional order split using CLAMPED hours ───────────────────
        //
        // Use the clamped hours sum as denominator, not the raw totalHours,
        // so API inflation doesn't distort the split ratio.
        //
        // Edge case: both sides zero → credit all orders to start date
        // (rider was essentially offline — rounding kept them at zero hours).
        decimal splitDenominator = hoursBeforeMidnight + hoursAfterMidnight;

        decimal ratio = splitDenominator > 0m
            ? hoursBeforeMidnight / splitDenominator
            : 1m;

        int ordersBeforeMidnight = (int)Math.Round(item.Orders * ratio);
        int ordersAfterMidnight = item.Orders - ordersBeforeMidnight;

        // Segment A — previous date, key = canonical shift start
        yield return new ShiftSegment(
            Source: item,
            Date: shiftStartDate,
            ActiveShiftStartedAt: CanonicalUtc(shiftStartUtc),
            WorkingHours: hoursBeforeMidnight,
            Orders: ordersBeforeMidnight);

        // Segment B — today, key = canonical midnight UTC
        yield return new ShiftSegment(
            Source: item,
            Date: todayLocal,
            ActiveShiftStartedAt: midnightUtc,
            WorkingHours: hoursAfterMidnight,
            Orders: ordersAfterMidnight);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // READ: SINGLE DAY
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<CompanyDayStats?> GetByCompanyAndDateAsync(
        string companyId, DateOnly date)
    {
        // Step 1: records that exist for the requested date
        var dateShifts = await db.RiderShiftStats
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Date == date)
            .ToListAsync();

        var presentRiderIds = dateShifts.Select(r => r.RiderId).ToHashSet();

        // Step 2: riders with NO record on this date → pull history for name lookup
        var missingRiderRows = await db.RiderShiftStats
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId
                     && r.Date != date
                     && !presentRiderIds.Contains(r.RiderId))
            .ToListAsync();

        // Step 3: one zero-stat placeholder per missing rider (keeps roster complete)
        var missingRiders = missingRiderRows
            .GroupBy(r => r.RiderId)
            .Select(g => new RiderShiftStat
            {
                RiderId = g.Key,
                RiderName = g.OrderByDescending(r => r.LastUpdatedAt)
                                        .First().RiderName,
                CompanyId = companyId,
                ActiveShiftStartedAt = DateTime.MinValue,
                Date = date,
                Orders = 0,
                WorkingHours = 0,
                Wallet = 0,
                LastUpdatedAt = DateTime.MinValue,
            })
            .ToList();

        var allShifts = dateShifts.Concat(missingRiders).ToList();
        return allShifts.Count == 0 ? null : BuildStats(companyId, date, allShifts);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // READ: COMPANY SUMMARY (last N days)
    // ═══════════════════════════════════════════════════════════════════════
    public async Task<IEnumerable<CompanyDayStats>> GetCompanySummaryAsync(
        string companyId, int lastDays = 30)
    {
        var from = TodayLocal().AddDays(-lastDays);

        var shifts = await db.RiderShiftStats
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Date >= from)
            .ToListAsync();

        return shifts
            .GroupBy(r => r.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => BuildStats(companyId, g.Key, g.ToList()));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BUILD STATS
    // ═══════════════════════════════════════════════════════════════════════
    //
    //  ORDERS — two-level aggregation (replaces the old flat g.Sum):
    //  ──────────────────────────────────────────────────────────────
    //  Problem with the original flat g.Sum(r => r.Orders):
    //    If a rider has multiple DB records for the same date — orphan rows
    //    from stale shift keys, or duplicate midnight rows from sub-second
    //    key drift — naively summing all of them inflates the order count.
    //
    //  Fix:
    //    1. For each rider, group records by ActiveShiftStartedAt (shift key).
    //    2. Within each shift group take MAX orders.
    //       Orders are cumulative per-shift in the API and overwritten on
    //       every poll.  MAX safely deduplicates any accidental duplicate rows
    //       under the same key (e.g. from a write race) without losing data.
    //    3. Sum the per-shift maxima across distinct shift keys.
    //       Riders who genuinely work two separate shifts in a day still get
    //       both counted; orphaned stale rows that survived the write-time
    //       cleanup are absorbed into their own max-group and cannot
    //       accumulate further.
    //
    //  HOURS — CapHours() applied after summing (read-time safety net):
    //  ─────────────────────────────────────────────────────────────────
    //    Corrects any stale DB rows that were written before the write-time
    //    guards existed.  A row with 14.9 h for today is silently corrected
    //    to ≤ elapsed-hours-since-midnight on every read, no migration needed.
    //
    private static CompanyDayStats BuildStats(
        string companyId, DateOnly date, IList<RiderShiftStat> shifts)
    {
        var perRider = shifts
            .GroupBy(r => r.RiderId)
            .Select(g =>
            {
                // ── Orders: max per shift key, then sum across shift keys ──────
                int totalOrders = g
                    .GroupBy(r => r.ActiveShiftStartedAt)
                    .Sum(shiftGroup => shiftGroup.Max(r => r.Orders));

                // ── Hours: sum segments, then cap to physical reality ──────────
                decimal rawHours = g.Sum(r => r.WorkingHours);
                decimal totalHours = Math.Round(CapHours(rawHours, date), 1);

                return new RiderShiftStatDto(
                    RiderId: g.Key,
                    RiderName: g.First().RiderName,
                    CompanyId: companyId,
                    ActiveShiftStartedAt: g.Min(r => r.ActiveShiftStartedAt),
                    Date: date,
                    Wallet: g.Max(r => r.Wallet),   // latest snapshot
                    Orders: totalOrders,
                    WorkingHours: totalHours);
            })
            .OrderByDescending(r => r.Orders)
            .ToList();

        return new CompanyDayStats(
            companyId,
            date,
            TotalRiders: perRider.Count,
            TotalOrders: perRider.Sum(r => r.Orders),
            TotalWallet: perRider.Sum(r => r.Wallet),
            TotalWorkingHours: Math.Round(perRider.Sum(r => r.WorkingHours), 1),
            Riders: perRider);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // INTERNAL SEGMENT RECORD
    // ═══════════════════════════════════════════════════════════════════════
    //
    //  Orders and WorkingHours are explicit constructor parameters — not
    //  derived from Source — so each segment carries only its proportional
    //  share of the original totals.
    //
    private record ShiftSegment(
        RiderShiftStatIncoming Source,
        DateOnly Date,
        DateTime ActiveShiftStartedAt,
        decimal WorkingHours,
        int Orders)
    {
        public string RiderId => Source.RiderId;
        public string RiderName => Source.RiderName;
        public string CompanyId => Source.CompanyId;
        public decimal Wallet => Source.Wallet;
    }
}