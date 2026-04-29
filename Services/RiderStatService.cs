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

    // ── UPSERT ─────────────────────────────────────────────────────────────
    public async Task UpsertBatchAsync(IEnumerable<RiderShiftStatIncoming> items)
    {
        var list = items
            .Where(i => i.ActiveShiftStartedAt.HasValue)
            .ToList();

        if (list.Count == 0) return;

        // Each incoming item may produce 1 or 2 DB records (midnight split)
        var allSegments = list.SelectMany(BuildSegments).ToList();

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
                record.Orders = seg.Orders;
                record.WorkingHours = seg.WorkingHours;
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

    // ── MIDNIGHT SPLIT LOGIC ───────────────────────────────────────────────
    // A shift that crosses midnight in UTC+3 is split into two segments:
    //
    //   Segment A  →  date of shift start  →  hours from shift start → midnight
    //   Segment B  →  next date            →  hours from midnight    → now (capped)
    //
    // Key rule: if the reported workedSeconds would exceed the time elapsed
    // since midnight on the CURRENT date, cap it to elapsed time.
    // This prevents a shift that just started at 11 PM from reporting 2 h
    // on today's date when only 1 h has actually passed today.
    //
    // The shift identity key (ActiveShiftStartedAt) is the SAME for both
    // segments so that upsert can find and update each date row correctly.
    // ──────────────────────────────────────────────────────────────────────
    private static IEnumerable<ShiftSegment> BuildSegments(RiderShiftStatIncoming item)
    {
        var shiftStartUtc = item.ActiveShiftStartedAt!.Value;
        var nowUtc = DateTime.UtcNow;
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
            var hoursToday = Math.Min(totalHours, Math.Max(0, elapsedHours));

            yield return new ShiftSegment(
                item, shiftStartDate, item.ActiveShiftStartedAt!.Value, hoursToday);

            yield break;
        }

        // ── Midnight crossing detected ─────────────────────────────────────
        // Shift started on a PREVIOUS calendar date (local time).
        // Split at local midnight between the two dates.

        // midnight that separates the two days (in UTC)
        var midnightLocal = shiftStartDate.ToDateTime(TimeOnly.MinValue).AddDays(1);
        // convert that local midnight back to UTC
        var midnightUtc = TimeZoneInfo.ConvertTimeToUtc(midnightLocal, SaudiTz);

        // Hours the rider worked on the START date (before midnight)
        var hoursBeforeMidnight = (decimal)(midnightUtc - shiftStartUtc).TotalHours;
        hoursBeforeMidnight = Math.Max(0, Math.Min(totalHours, hoursBeforeMidnight));

        // Hours worked on the NEXT date (after midnight, capped to elapsed time today)
        var hoursAfterMidnight = totalHours - hoursBeforeMidnight;
        var elapsedTodayHours = (decimal)(nowUtc - midnightUtc).TotalHours;
        hoursAfterMidnight = Math.Max(0, Math.Min(hoursAfterMidnight, elapsedTodayHours));

        // Segment A — previous date, shift identity = original shift start
        yield return new ShiftSegment(
            item, shiftStartDate, shiftStartUtc, hoursBeforeMidnight);

        // Segment B — today's date, shift identity = midnight UTC
        // Using midnight as the key keeps today's segment uniquely identifiable
        // while still being tied to the same physical shift.
        yield return new ShiftSegment(
            item, todayLocal, midnightUtc, hoursAfterMidnight);
    }

    // ── READ: GET BY COMPANY + DATE ────────────────────────────────────────
    public async Task<CompanyDayStats?> GetByCompanyAndDateAsync(
        string companyId, DateOnly date)
    {
        var shifts = await db.RiderShiftStats
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Date == date)
            .OrderByDescending(r => r.Orders)
            .ToListAsync();

        return shifts.Count == 0 ? null : BuildStats(companyId, date, shifts);
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
        string companyId, DateOnly date, IList<RiderShiftStat> shifts) => new(
            companyId,
            date,
            TotalRiders: shifts.Select(r => r.RiderId).Distinct().Count(),
            TotalOrders: shifts.Sum(r => r.Orders),
            TotalWallet: shifts.Sum(r => r.Wallet),
            TotalWorkingHours: shifts.Sum(r => r.WorkingHours),
            Riders: shifts.Select(r => new RiderShiftStatDto(
                r.RiderId, r.RiderName, r.CompanyId,
                r.ActiveShiftStartedAt, r.Date,
                r.Wallet, r.Orders, r.WorkingHours))
        );

    // ── INTERNAL SEGMENT RECORD ────────────────────────────────────────────
    private record ShiftSegment(
        RiderShiftStatIncoming Source,
        DateOnly Date,
        DateTime ActiveShiftStartedAt,
        decimal WorkingHours)
    {
        public string RiderId => Source.RiderId;
        public string RiderName => Source.RiderName;
        public string CompanyId => Source.CompanyId;
        public int Orders => Source.Orders;
        public decimal Wallet => Source.Wallet;
    }
}