using LiveDahsboard.Data;
using LiveDahsboard.DTOs;
using LiveDahsboard.Models;
using Microsoft.EntityFrameworkCore;

namespace LiveDahsboard.Services;

public class RiderStatService(ApplicationDbContext db) : IRiderStatService
{
    // ── RESET THRESHOLDS ───────────────────────────────────────────────────
    // A drop larger than this is treated as a genuine new shift, not a glitch.
    private const decimal HOURS_RESET_THRESHOLD = 0.5m;   // 30 minutes
    private const int ORDERS_ZERO_GRACE_TICKS = 2;     // allow 2 zero readings before committing

    // ── UPSERT ─────────────────────────────────────────────────────────────
    public async Task UpsertBatchAsync(IEnumerable<RiderStatDto> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;

        var dates = list.Select(i => i.Date).Distinct().ToList();
        var riderIds = list.Select(i => i.RiderId).Distinct().ToList();
        var companyIds = list.Select(i => i.CompanyId).Distinct().ToList();

        var existing = await db.RiderStats
            .Where(r => riderIds.Contains(r.RiderId)
                     && companyIds.Contains(r.CompanyId)
                     && dates.Contains(r.Date))
            .ToListAsync();

        var map = existing.ToDictionary(r => (r.RiderId, r.Date, r.CompanyId));
        var now = DateTime.UtcNow;

        foreach (var dto in list)
        {
            var key = (dto.RiderId, dto.Date, dto.CompanyId);

            if (map.TryGetValue(key, out var record))
            {
                UpdateExisting(record, dto, now);
            }
            else
            {
                var newRecord = CreateNew(dto, now);
                db.RiderStats.Add(newRecord);
                map[key] = newRecord;   // guard against duplicates in same batch
            }
        }

        // ── ONE save for the entire batch ──────────────────────────────────
        await db.SaveChangesAsync();
    }

    // ── UPDATE EXISTING RECORD ─────────────────────────────────────────────
    private static void UpdateExisting(RiderStat record, RiderStatDto dto, DateTime now)
    {
        // Always keep the name fresh in case it changed in the API
        if (!string.IsNullOrWhiteSpace(dto.RiderName))
            record.RiderName = dto.RiderName;

        UpdateHours(record, dto.WorkingHours);
        UpdateOrders(record, dto.Orders);

        record.Wallet = dto.Wallet;
        record.LastUpdatedAt = now;
    }

    // ── HOURS ACCUMULATION ─────────────────────────────────────────────────
    // API sends hours-since-shift-start. When a new shift begins the value
    // resets to ~0, so a large drop means "commit the completed shift."
    private static void UpdateHours(RiderStat record, decimal incomingHours)
    {
        var drop = record.LastSeenWorkingHours - incomingHours;

        if (drop > HOURS_RESET_THRESHOLD)
        {
            // New shift detected: bank the completed shift's hours
            record.WorkingHoursBase += record.LastSeenWorkingHours;
        }

        record.LastSeenWorkingHours = incomingHours;
        record.WorkingHours = record.WorkingHoursBase + incomingHours;
    }

    // ── ORDERS ACCUMULATION ────────────────────────────────────────────────
    // Three cases:
    //  1. Normal increment:    dto.Orders >= LastSeen          → same shift
    //  2. Drop to zero:        dto.Orders == 0, was > 0        → snapshot, wait
    //  3. Non-zero drop:       0 < dto.Orders < LastSeen       → new shift confirmed
    //
    // "ZeroTicks" lets us absorb a single glitch zero before committing.
    private static void UpdateOrders(RiderStat record, int incomingOrders)
    {
        if (incomingOrders < record.LastSeenOrders)
        {
            if (incomingOrders == 0)
            {
                // Could be a glitch — snapshot and wait up to GRACE ticks
                if (record.OrdersSnapshottedBeforeReset == 0)
                    record.OrdersSnapshottedBeforeReset = record.LastSeenOrders;

                record.LastSeenOrders = 0;
                // Don't change Orders yet — keep showing last known value
                return;
            }
            else
            {
                // Unambiguous drop to non-zero: definitely a new shift
                CommitCompletedShift(record);
            }
        }
        else if (record.OrdersSnapshottedBeforeReset > 0)
        {
            // We were in a "zero grace" window — now resolving it
            if (incomingOrders > 0 && incomingOrders < record.OrdersSnapshottedBeforeReset)
            {
                // Recovered with fewer orders → genuine new shift
                CommitCompletedShift(record);
            }
            else if (incomingOrders >= record.OrdersSnapshottedBeforeReset)
            {
                // Recovered at same or higher level → was a glitch, cancel snapshot
                record.OrdersSnapshottedBeforeReset = 0;
            }
            // incomingOrders == 0 again: still in grace, keep waiting
        }

        record.LastSeenOrders = incomingOrders;
        record.Orders = record.OrdersBase + incomingOrders;
    }

    private static void CommitCompletedShift(RiderStat record)
    {
        // Bank the highest known value from the completed shift
        var completedShiftOrders = record.OrdersSnapshottedBeforeReset > 0
            ? record.OrdersSnapshottedBeforeReset
            : record.LastSeenOrders;

        record.OrdersBase += completedShiftOrders;
        record.OrdersSnapshottedBeforeReset = 0;
    }

    // ── CREATE NEW RECORD ──────────────────────────────────────────────────
    // Key fix: Orders starts at dto.Orders (not 0) so that riders who are
    // mid-shift when the extension first sees them are counted correctly.
    private static RiderStat CreateNew(RiderStatDto dto, DateTime now) => new()
    {
        RiderId = dto.RiderId,
        RiderName = dto.RiderName,
        CompanyId = dto.CompanyId,
        Date = dto.Date,
        Wallet = dto.Wallet,

        // Start from whatever the API reports right now
        Orders = dto.Orders,
        OrdersBase = 0,
        LastSeenOrders = dto.Orders,
        OrdersSnapshottedBeforeReset = 0,

        // OrdersDayStart kept at 0 — no longer subtracted in UpdateOrders
        OrdersDayStart = 0,

        WorkingHours = dto.WorkingHours,
        WorkingHoursBase = 0,
        LastSeenWorkingHours = dto.WorkingHours,

        LastUpdatedAt = now,
    };

    // ── READ ENDPOINTS (unchanged logic) ───────────────────────────────────
    public async Task<CompanyDayStats?> GetByCompanyAndDateAsync(
        string companyId, DateOnly date)
    {
        var riders = await db.RiderStats
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Date == date)
            .OrderByDescending(r => r.WorkingHours)
            .ToListAsync();

        return riders.Count == 0 ? null : BuildStats(companyId, date, riders);
    }

    public async Task<IEnumerable<CompanyDayStats>> GetCompanySummaryAsync(
        string companyId, int lastDays = 30)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-lastDays));

        var riders = await db.RiderStats
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Date >= from)
            .ToListAsync();

        return riders
            .GroupBy(r => r.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => BuildStats(companyId, g.Key, g.ToList()));
    }

    private static CompanyDayStats BuildStats(
        string companyId, DateOnly date, IList<RiderStat> riders) => new(
            companyId,
            date,
            TotalRiders: riders.Count,
            TotalOrders: riders.Sum(r => r.Orders),
            TotalWallet: riders.Sum(r => r.Wallet),
            TotalWorkingHours: riders.Sum(r => r.WorkingHours),
            Riders: riders.Select(r => new RiderStatDto(
                r.RiderId, r.RiderName, r.CompanyId, r.Date,
                r.Wallet, r.Orders, r.WorkingHours))
        );
}