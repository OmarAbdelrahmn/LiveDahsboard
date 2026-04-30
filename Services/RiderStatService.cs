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
    /// Shifts older than this are considered stale / un-reset API data.
    /// They are silently skipped to prevent hours and orders from inflating.
    /// 20 h gives a comfortable margin for genuine long shifts while blocking
    /// multi-day carry-overs.
    /// </summary>
    private const int MaxShiftHours = 20;

    // ── UPSERT ─────────────────────────────────────────────────────────────
    public async Task UpsertBatchAsync(IEnumerable<RiderShiftStatIncoming> items)
    {
        var list = items
            .Where(i => i.ActiveShiftStartedAt.HasValue)
            .ToList();

        if (list.Count == 0) return;

        // Each incoming item may produce 1 or 2 DB records (midnight split).
        // Stale shifts (> MaxShiftHours old) are dropped inside BuildSegments.
        var allSegments = list.SelectMany(BuildSegments).ToList();

        if (allSegments.Count == 0) return;

        var riderIds = allSegments.Select(s => s.RiderId).Distinct().ToList();
        var companyIds = allSegments.Select(s => s.CompanyId).Distinct().ToList();
        var shifts = allSegments.Select(s => s.ActiveShiftStartedAt).Distinct().ToList();

        var existing = await db.RiderShiftStats
            .Where(r => riderIds.Contains(r.RiderId)
                     && companyIds.Contains(r.CompanyId)
                     && shifts.Contains(r.ActiveShiftStartedAt))
            .ToListAsync();

        var map = existing.ToDictionary(r => (r.RiderId, r.CompanyId, r.ActiveShiftStartedAt));
        var now = DateTime.UtcNow;

        foreach (var seg in allSegments)
        {
            var key = (seg.RiderId, seg.CompanyId, seg.ActiveShiftStartedAt);

            if (map.TryGetValue(key, out var record))
            {
                record.RiderName = seg.RiderName;
                record.Orders = seg.Orders;           // split value, not raw total
                record.WorkingHours = seg.WorkingHours;     // capped / split value
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
                    WorkingHours = seg.WorkingHours,
                    Wallet = seg.Wallet,
                    LastUpdatedAt = now,
                };
                db.RiderShiftStats.Add(newRecord);
                map[key] = newRecord;
            }
        }

        await db.SaveChangesAsync();
    }

    // ── SEGMENT BUILDER ────────────────────────────────────────────────────
    //
    //  Rules:
    //  1. Shifts older than MaxShiftHours are silently dropped — stale API data.
    //  2. Same-day shift → single segment, hours capped to elapsed time.
    //  3. Midnight-crossing shift → two segments:
    //       Segment A  date = shift-start date   key = shiftStartUtc
    //       Segment B  date = today              key = midnightUtc
    //     Hours are split at the local-midnight boundary and each side is
    //     independently capped.
    //     Orders are split PROPORTIONALLY by hours so that today's row only
    //     counts orders likely completed after midnight, not the whole shift total.
    //
    private static IEnumerable<ShiftSegment> BuildSegments(RiderShiftStatIncoming item)
    {
        var shiftStartUtc = item.ActiveShiftStartedAt!.Value;
        var nowUtc = DateTime.UtcNow;

        // ── Guard: drop shifts that are unrealistically old ────────────────
        if ((nowUtc - shiftStartUtc).TotalHours > MaxShiftHours)
            yield break;

        var totalHours = item.WorkedSeconds / 3600m;

        // Convert to Saudi local time (UTC+3)
        var shiftStartLocal = TimeZoneInfo.ConvertTimeFromUtc(shiftStartUtc, SaudiTz);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, SaudiTz);

        var shiftStartDate = DateOnly.FromDateTime(shiftStartLocal);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        // ── No midnight crossing ───────────────────────────────────────────
        if (shiftStartDate == todayLocal)
        {
            // Cap: can't have worked more hours than have elapsed since shift start
            var elapsedHours = (decimal)(nowUtc - shiftStartUtc).TotalHours;
            var hoursToday = Math.Min(totalHours, Math.Max(0m, elapsedHours));

            yield return new ShiftSegment(
                Source: item,
                Date: shiftStartDate,
                ActiveShiftStartedAt: shiftStartUtc,
                WorkingHours: hoursToday,
                Orders: item.Orders);   // full orders — all happened today

            yield break;
        }

        // ── Midnight crossing detected ─────────────────────────────────────
        // Find the local midnight that separates the two calendar dates.
        var midnightLocal = shiftStartDate.ToDateTime(TimeOnly.MinValue).AddDays(1);
        var midnightUtc = TimeZoneInfo.ConvertTimeToUtc(midnightLocal, SaudiTz);

        // Hours the rider worked on the START date (shift-start → midnight)
        var hoursBeforeMidnight = (decimal)(midnightUtc - shiftStartUtc).TotalHours;
        hoursBeforeMidnight = Math.Max(0m, Math.Min(totalHours, hoursBeforeMidnight));

        // Hours worked on the CURRENT date (midnight → now), capped to elapsed
        var hoursAfterMidnight = totalHours - hoursBeforeMidnight;
        var elapsedTodayHours = (decimal)(nowUtc - midnightUtc).TotalHours;
        hoursAfterMidnight = Math.Max(0m, Math.Min(hoursAfterMidnight, elapsedTodayHours));

        // ── Proportional order split ───────────────────────────────────────
        // We don't know exactly when each order happened, so we use the hours
        // ratio as the best available proxy.
        // Edge case: if totalHours rounds to zero, credit all orders to the
        // start-date segment (the rider barely worked — probably still offline).
        decimal ratio = totalHours > 0m
            ? hoursBeforeMidnight / totalHours
            : 1m;

        int ordersBeforeMidnight = (int)Math.Round(item.Orders * ratio);
        int ordersAfterMidnight = item.Orders - ordersBeforeMidnight;

        // Segment A — previous date, shift identity = original shift start
        yield return new ShiftSegment(
            Source: item,
            Date: shiftStartDate,
            ActiveShiftStartedAt: shiftStartUtc,
            WorkingHours: hoursBeforeMidnight,
            Orders: ordersBeforeMidnight);

        // Segment B — today, shift identity = midnight UTC
        // Using midnight as the key keeps today's segment uniquely identifiable
        // while still being tied to the same physical shift.
        yield return new ShiftSegment(
            Source: item,
            Date: todayLocal,
            ActiveShiftStartedAt: midnightUtc,
            WorkingHours: hoursAfterMidnight,
            Orders: ordersAfterMidnight);
    }
    public async Task<CompanyDayStats?> GetByCompanyAndDateAsync(
        string companyId, DateOnly date)
    {
        // All shifts for this specific day
        var todayShifts = await db.RiderShiftStats
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Date == date)
            .ToListAsync();

        // Riders who have ANY record for this company but NOT on this date
        var presentRiderIds = todayShifts.Select(r => r.RiderId).ToHashSet();

        var missingRiders = await db.RiderShiftStats
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId
                     && r.Date != date
                     && !presentRiderIds.Contains(r.RiderId))
            .GroupBy(r => r.RiderId)
            .Select(g => new RiderShiftStat
            {
                RiderId = g.Key,
                RiderName = g.OrderByDescending(r => r.LastUpdatedAt)
                             .Select(r => r.RiderName)
                             .First(),
                CompanyId = companyId,
                ActiveShiftStartedAt = DateTime.MinValue,
                Date = date,
                Orders = 0,
                WorkingHours = 0,
                Wallet = 0,
                LastUpdatedAt = DateTime.MinValue,
            })
            .ToListAsync();

        var allShifts = todayShifts.Concat(missingRiders).ToList();

        return allShifts.Count == 0 ? null : BuildStats(companyId, date, allShifts);
    }

    // ── READ: COMPANY SUMMARY (last N days) ────────────────────────────────
    public async Task<IEnumerable<CompanyDayStats>> GetCompanySummaryAsync(
        string companyId, int lastDays = 30)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SaudiTz);
        var from = DateOnly.FromDateTime(nowLocal).AddDays(-lastDays);

        var shifts = await db.RiderShiftStats
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Date >= from)
            .ToListAsync();

        return shifts
            .GroupBy(r => r.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => BuildStats(companyId, g.Key, g.ToList()));
    }

    // ── BUILD STATS ────────────────────────────────────────────────────────
    private static CompanyDayStats BuildStats(
        string companyId, DateOnly date, IList<RiderShiftStat> shifts)
    {
        var perRider = shifts
            .GroupBy(r => r.RiderId)
            .Select(g => new RiderShiftStatDto(
                RiderId: g.Key,
                RiderName: g.First().RiderName,
                CompanyId: companyId,
                ActiveShiftStartedAt: g.Min(r => r.ActiveShiftStartedAt),
                Date: date,
                Wallet: g.Max(r => r.Wallet),
                Orders: g.Max(r => r.Orders),   // ← Max, not Sum
                WorkingHours: g.Sum(r => r.WorkingHours))) // ← Sum is correct
            .OrderByDescending(r => r.Orders)
            .ToList();

        return new CompanyDayStats(
            companyId,
            date,
            TotalRiders: perRider.Count,
            TotalOrders: perRider.Sum(r => r.Orders),
            TotalWallet: perRider.Sum(r => r.Wallet),
            TotalWorkingHours: perRider.Sum(r => r.WorkingHours),
            Riders: perRider);
    }

    // ── INTERNAL SEGMENT RECORD ────────────────────────────────────────────
    // Orders is now an explicit parameter — NOT derived from Source.Orders —
    // so each segment carries only its share of the total.
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