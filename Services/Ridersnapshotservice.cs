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
// INCOMING DTO  (what the front-end / controller hands us)
// ─────────────────────────────────────────────────────────────────────────────

public record RiderSnapshotIncoming(
    string RiderId,
    string RiderName,
    string CompanyId,
    int Orders,
    /// <summary>Raw seconds from the API — converted to hours on the way in.</summary>
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

    // ── Reset-detection thresholds ────────────────────────────────────────────
    //
    // A "session reset" is when the API's cumulative counter drops, meaning the
    // rider started a brand-new shift.  We treat any drop above these thresholds
    // as a reset so that normal floating-point jitter is ignored.
    //
    private const decimal HoursResetThreshold = 0.05m;   // ~3 minutes
    private const int OrdersResetThreshold = 1;           // any order decrease

    // ═══════════════════════════════════════════════════════════════════════════
    // WRITE — just append, no heuristics
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task InsertBatchAsync(IEnumerable<RiderSnapshotIncoming> items)
    {
        var now = NowUtc;
        var today = DateOnly.FromDateTime(ToLocal(now));

        var snapshots = items.Select(item => new RiderSnapshot
        {
            RiderId = item.RiderId,
            RiderName = item.RiderName,
            CompanyId = item.CompanyId,
            Date = today,
            RecordedAtUtc = now,
            Orders = item.Orders,
            WorkingHours = Math.Max(0m, item.WorkedSeconds / 3600m),
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
    // BUILD STATS  — the masterpiece
    // ═══════════════════════════════════════════════════════════════════════════
    //
    //  How working hours and orders are derived
    //  ─────────────────────────────────────────
    //  The API sends CUMULATIVE counters for the rider's current shift.
    //  When a new shift starts (end-of-shift, app restart, midnight, etc.)
    //  those counters reset to a low value.
    //
    //  Algorithm per rider:
    //    1. Sort snapshots chronologically.
    //    2. Walk the series; detect a "session reset" whenever the current
    //       value drops below the previous value by more than the threshold.
    //    3. Each session's contribution = last_value − first_value.
    //       • Same-day start  → first ≈ 0, contribution ≈ last_value.
    //       • Midnight carry-over → first is yesterday's carry, contribution
    //         = only the hours/orders accumulated on THIS date.
    //       • Multiple shifts in one day → each shift is its own session;
    //         contributions are summed.
    //    4. Total = Σ session contributions.
    //
    //  Example timeline for one rider:
    //    08:00  hours=0.10  orders=0   ← shift 1 starts
    //    09:00  hours=1.05  orders=3
    //    10:00  hours=2.02  orders=7   ← shift 1 ends   contribution: 2.02−0.10 = 1.92h / 7 orders
    //    10:30  hours=0.08  orders=0   ← shift 2 starts (reset detected)
    //    12:00  hours=1.55  orders=5                    contribution: 1.55−0.08 = 1.47h / 5 orders
    //    Total: 3.39 h  /  12 orders
    //
    private static CompanyDayStats BuildStats(
        string companyId, DateOnly date, IList<RiderSnapshot> snapshots)
    {
        var perRider = snapshots
            .GroupBy(r => r.RiderId)
            .Select(g =>
            {
                // Snapshots are already ordered by RecordedAtUtc from the query,
                // but re-sort here in case this method is called with arbitrary data.
                var ordered = g.OrderBy(r => r.RecordedAtUtc).ToList();

                decimal totalHours = SumSessions(
                    ordered.Select(r => r.WorkingHours).ToList(),
                    HoursResetThreshold);

                int totalOrders = (int)SumSessions(
                    ordered.Select(r => (decimal)r.Orders).ToList(),
                    OrdersResetThreshold);

                var first = ordered.First();
                var last = ordered.Last();

                return new RiderSnapshotDto(
                    RiderId: g.Key,
                    RiderName: last.RiderName,       // use latest name in case of override
                    CompanyId: companyId,
                    Date: date,
                    Wallet: last.Wallet,             // latest balance snapshot
                    Orders: totalOrders,
                    WorkingHours: Math.Round(totalHours, 1),
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
    // SESSION SUM — core algorithm
    // ═══════════════════════════════════════════════════════════════════════════
    //
    //  Walks an ordered series of cumulative values and returns the total
    //  NET increase across all sessions (resetting whenever the value drops).
    //
    //  Parameters
    //  ──────────
    //  values         Chronologically ordered cumulative readings.
    //  resetThreshold Any drop ≥ this is treated as a new session start.
    //                 Smaller drops are considered noise and ignored.
    //
    private static decimal SumSessions(IList<decimal> values, decimal resetThreshold)
    {
        if (values.Count == 0) return 0m;

        // A single snapshot → just return its value directly.
        // There's no "previous" to subtract, so the API value IS the total
        // for this session (which started at some point before we began recording).
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
                // Close the current session: add what accumulated up to the last
                // reading before the reset.
                total += Math.Max(0m, prev - sessionStart);

                // New session begins at the current (post-reset) value.
                sessionStart = current;
            }

            prev = current;
        }

        // Close the final (open) session.
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
    /// <summary>UTC time of the first snapshot recorded today for this rider.</summary>
    DateTime FirstSeenAt,
    /// <summary>UTC time of the most recent snapshot recorded today for this rider.</summary>
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