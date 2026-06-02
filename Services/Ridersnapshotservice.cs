// Services/RiderSnapshotService.cs
using LiveDahsboard.Data;
using LiveDahsboard.DTOs;
using LiveDahsboard.Models;
using Microsoft.EntityFrameworkCore;

namespace LiveDahsboard.Services;

// ─────────────────────────────────────────────────────────────────────────────
// INTERFACE
// ─────────────────────────────────────────────────────────────────────────────

public interface IRiderSnapshotService
{
    /// <summary>Save one snapshot per rider for the current poll.</summary>
    Task InsertBatchAsync(IEnumerable<RiderSnapshotIncoming> items);

    /// <summary>Stats for a single company on a single local date.</summary>
    Task<CompanyDayStats?> GetByCompanyAndDateAsync(string companyId, DateOnly date);

    /// <summary>Per-day summary for the last <paramref name="lastDays"/> days.</summary>
    Task<IEnumerable<CompanyDayStats>> GetCompanySummaryAsync(string companyId, int lastDays = 30);
}

// ─────────────────────────────────────────────────────────────────────────────
// INCOMING DTO
// ─────────────────────────────────────────────────────────────────────────────

public record RiderSnapshotIncoming(
    string RiderId,
    string RiderName,
    string CompanyId,
    int Orders,
    /// <summary>Raw seconds from the API — kept for potential future use but
    /// working hours are now derived from snapshot timestamps instead.</summary>
    decimal WorkedSeconds,
    decimal Wallet);

// ─────────────────────────────────────────────────────────────────────────────
// SERVICE
// ─────────────────────────────────────────────────────────────────────────────

public class RiderSnapshotService(ApplicationDbContext db) : IRiderSnapshotService
{
    // ── Timezone ──────────────────────────────────────────────────────────────
    private static readonly TimeZoneInfo SaudiTz =
        TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");

    private static DateTime NowUtc => DateTime.UtcNow;

    private static DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, SaudiTz);

    private static DateOnly TodayLocal() =>
        DateOnly.FromDateTime(ToLocal(NowUtc));

    // ── Orders reset-detection threshold ─────────────────────────────────────
    private const int OrdersResetThreshold = 1;

    // ── Active-time gap threshold ─────────────────────────────────────────────
    //
    // Snapshots are pushed every 30 seconds.  If two consecutive snapshots are
    // more than this many seconds apart we treat the gap as a break / offline
    // period and do NOT count it toward working hours.
    //
    // 300 s (5 min) gives enough headroom for a missed poll or a brief
    // network hiccup without accidentally crediting a long break as work time.
    //
    private const double ActiveGapThresholdSeconds = 300.0;

    // ═══════════════════════════════════════════════════════════════════════════
    // WRITE
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task InsertBatchAsync(IEnumerable<RiderSnapshotIncoming> items)
    {
        var now = NowUtc;

        // ── Midnight-split logic ──────────────────────────────────────────────
        //
        // Converting to local time here means a rider whose shift crosses
        // midnight gets their pre-midnight snapshots stamped with Day 1 and
        // their post-midnight snapshots stamped with Day 2.  The working-hours
        // algorithm in BuildStats then naturally produces correct per-day totals
        // because it only ever sees snapshots for a single calendar date.
        //
        var today = DateOnly.FromDateTime(ToLocal(now));

        var snapshots = items.Select(item => new RiderSnapshot
        {
            RiderId = item.RiderId,
            RiderName = item.RiderName,
            CompanyId = item.CompanyId,
            Date = today,
            RecordedAtUtc = now,
            Orders = item.Orders,

            // WorkingHours stored here is kept as 0 — the real calculation
            // happens in BuildStats from the RecordedAtUtc timestamps.
            WorkingHours = 0m,

            Wallet = item.Wallet,
        }).ToList();

        if (snapshots.Count == 0) return;

        await db.RiderSnapshots.AddRangeAsync(snapshots);
        await db.SaveChangesAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // READ — single day
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<CompanyDayStats?> GetByCompanyAndDateAsync(
        string companyId, DateOnly date)
    {
        var snapshots = await db.RiderSnapshots
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Date == date)
            .OrderBy(r => r.RiderId)
            .ThenBy(r => r.RecordedAtUtc)
            .ToListAsync();

        return snapshots.Count == 0
            ? null
            : BuildStats(companyId, date, snapshots);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // READ — company summary (last N days)
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<IEnumerable<CompanyDayStats>> GetCompanySummaryAsync(
        string companyId, int lastDays = 30)
    {
        var from = TodayLocal().AddDays(-lastDays);

        var snapshots = await db.RiderSnapshots
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Date >= from)
            .OrderBy(r => r.RiderId)
            .ThenBy(r => r.RecordedAtUtc)
            .ToListAsync();

        return snapshots
            .GroupBy(r => r.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => BuildStats(companyId, g.Key, g.ToList()));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // BUILD STATS
    // ═══════════════════════════════════════════════════════════════════════════
    //
    //  Working-hours derivation (timestamp-based)
    //  ──────────────────────────────────────────
    //  Because the API's worked-seconds counter is unreliable (always 0), hours
    //  are computed from the RecordedAtUtc timestamps instead.
    //
    //  Algorithm per rider (single calendar date):
    //    1. Sort snapshots by RecordedAtUtc.
    //    2. Walk consecutive pairs (t_i, t_{i+1}).
    //    3. If the gap ≤ ActiveGapThresholdSeconds → rider was active; add it.
    //       If the gap  > threshold                → break / offline; skip it.
    //    4. Total active seconds ÷ 3 600 = working hours for this date.
    //
    //  Midnight-crossing shifts
    //  ─────────────────────────
    //  InsertBatchAsync stamps Date using the local Saudi time, so a rider
    //  working 22:00 → 03:00 produces:
    //    • Day 1 snapshots: 22:00 – 23:59  →  ≈ 2 h counted for Day 1
    //    • Day 2 snapshots: 00:00 – 03:00  →  ≈ 3 h counted for Day 2
    //  The 30-second gap at the boundary is silently dropped — negligible.
    //
    //  Example (rider, single day 00:00 – 03:00, 30-s polling):
    //    360 consecutive 30-second gaps × 30 s = 10 800 s = 3.00 h  ✓
    //
    private static CompanyDayStats BuildStats(
        string companyId, DateOnly date, IList<RiderSnapshot> snapshots)
    {
        var perRider = snapshots
            .GroupBy(r => r.RiderId)
            .Select(g =>
            {
                var ordered = g.OrderBy(r => r.RecordedAtUtc).ToList();

                // ── Hours: derived from timestamps, NOT the stored counter ──
                decimal totalHours = ComputeActiveHours(ordered);

                // ── Orders: still uses the cumulative-counter + reset logic ──
                int totalOrders = (int)SumSessions(
                    ordered.Select(r => (decimal)r.Orders).ToList(),
                    OrdersResetThreshold);

                var first = ordered.First();
                var last = ordered.Last();

                return new RiderSnapshotDto(
                    RiderId: g.Key,
                    RiderName: last.RiderName,
                    CompanyId: companyId,
                    Date: date,
                    Wallet: last.Wallet,
                    Orders: totalOrders,
                    WorkingHours: totalHours,
                    FirstSeenAt: first.RecordedAtUtc,
                    LastSeenAt: last.RecordedAtUtc);
            })
            .OrderByDescending(r => r.Orders)
            .ToList();

        return new CompanyDayStats(
            CompanyId: companyId,
            Date: date,
            TotalRiders: perRider.Count,
            TotalOrders: perRider.Sum(r => r.Orders),
            TotalWallet: perRider.Sum(r => r.Wallet),
            TotalWorkingHours: Math.Round(perRider.Sum(r => r.WorkingHours), 1),
            Riders: perRider);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // COMPUTE ACTIVE HOURS  — timestamp-based working time
    // ═══════════════════════════════════════════════════════════════════════════
    //
    //  Sums every consecutive snapshot gap that falls within the active
    //  threshold.  Gaps above the threshold (breaks, offline, midnight boundary)
    //  are silently skipped.
    //
    //  Edge cases:
    //    • 0 snapshots  → 0 h  (nothing to measure)
    //    • 1 snapshot   → 0 h  (a single point has no duration)
    //    • All gaps > threshold  → 0 h  (rider was polled once, then disappeared)
    //
    private static decimal ComputeActiveHours(IList<RiderSnapshot> ordered)
    {
        if (ordered.Count < 2) return 0m;

        double totalSeconds = 0;

        for (int i = 1; i < ordered.Count; i++)
        {
            double gap = (ordered[i].RecordedAtUtc - ordered[i - 1].RecordedAtUtc)
                         .TotalSeconds;

            if (gap <= ActiveGapThresholdSeconds)
                totalSeconds += gap;
        }

        return Math.Round((decimal)(totalSeconds / 3600.0), 2);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SESSION SUM — used for orders only
    // ═══════════════════════════════════════════════════════════════════════════

    private static decimal SumSessions(IList<decimal> values, decimal resetThreshold)
    {
        if (values.Count == 0) return 0m;
        if (values.Count == 1) return Math.Max(0m, values[0]);

        decimal total = 0m;
        decimal sessionStart = values[0];
        decimal prev = values[0];

        for (int i = 1; i < values.Count; i++)
        {
            decimal current = values[i];
            bool resetDetected = prev - current >= resetThreshold;

            if (resetDetected)
            {
                total += Math.Max(0m, prev - sessionStart);
                sessionStart = current;
            }

            prev = current;
        }

        total += Math.Max(0m, prev - sessionStart);
        return total;
    }
}

/// <summary>Per-rider stats derived from raw snapshots for a single day.</summary>
public record RiderSnapshotDto(
    string RiderId,
    string RiderName,
    string CompanyId,
    DateOnly Date,
    decimal Wallet,
    int Orders,
    decimal WorkingHours,
    DateTime FirstSeenAt,
    DateTime LastSeenAt);

/// <summary>Aggregated company-level stats for a single day, with per-rider breakdown.</summary>
public record CompanyDayStats(
    string CompanyId,
    DateOnly Date,
    int TotalRiders,
    int TotalOrders,
    decimal TotalWallet,
    decimal TotalWorkingHours,
    IReadOnlyList<RiderSnapshotDto> Riders);