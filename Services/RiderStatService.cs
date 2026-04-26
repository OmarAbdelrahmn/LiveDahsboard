using LiveDahsboard.Data;
using LiveDahsboard.DTOs;
using LiveDahsboard.Models;
using Microsoft.EntityFrameworkCore;

namespace LiveDahsboard.Services;


public class RiderStatService(ApplicationDbContext db) : IRiderStatService
{
    // ── UPSERT ─────────────────────────────────────────────────────────────
    // Called every 30 seconds from the Chrome extension.
    // The API always sends values-since-shift-start (resets each new shift).
    // We detect the reset and accumulate correctly.
    public async Task UpsertBatchAsync(IEnumerable<RiderStatDto> items)
    {
        var list = items.ToList();

        var dates = list.Select(i => i.Date).Distinct().ToList();
        var riderIds = list.Select(i => i.RiderId).Distinct().ToList();
        var companyIds = list.Select(i => i.CompanyId).Distinct().ToList();

        // Load ALL existing records for these riders/dates in ONE query
        var existing = await db.RiderStats
            .Where(r => riderIds.Contains(r.RiderId)
                     && companyIds.Contains(r.CompanyId)
                     && dates.Contains(r.Date))
            .ToListAsync();

        var existingMap = existing.ToDictionary(
            r => (r.RiderId, r.Date, r.CompanyId));

        var now = DateTime.UtcNow;

        foreach (var dto in list)
        {
            var key = (dto.RiderId, dto.Date, dto.CompanyId);

            if (existingMap.TryGetValue(key, out var record))
            {
                // ── HOURS ───────────────────────────────────────────────
                // Example:
                //   Shift 1 ends at 3.0h  → LastSeen = 3.0, Base = 0
                //   Shift 2 sends 0.5h    → 0.5 < 3.0 = RESET
                //     → Base becomes 3.0, LastSeen becomes 0.5
                //     → Total = 3.0 + 0.5 = 3.5  ✅
                //   Shift 2 sends 0.6h    → 0.6 > 0.5 = same shift
                //     → Total = 3.0 + 0.6 = 3.6  ✅
                // ── HOURS ───────────────────────────────────────────────
                const decimal RESET_THRESHOLD_HOURS = 1.0m;

                if (record.LastSeenWorkingHours - dto.WorkingHours > RESET_THRESHOLD_HOURS)
                {
                    record.WorkingHoursBase += record.LastSeenWorkingHours;
                }
                record.LastSeenWorkingHours = dto.WorkingHours;
                record.WorkingHours = record.WorkingHoursBase + dto.WorkingHours;



                // ── ORDERS ──────────────────────────────────────────────
                if (dto.Orders < record.LastSeenOrders)
                {
                    if (dto.Orders == 0)
                    {
                        // Might be a glitch or real shift end — wait one tick to decide
                        record.OrdersSnapshottedBeforeReset = record.LastSeenOrders;
                    }
                    else
                    {
                        // Non-zero drop → real new shift (e.g. 10 → 5)
                        // Commit only TODAY's contribution from the old shift
                        record.OrdersBase += record.LastSeenOrders - record.OrdersDayStart;
                        record.OrdersDayStart = 0;   // baseline no longer applies
                        record.OrdersSnapshottedBeforeReset = 0;
                    }
                }
                else if (dto.Orders > 0 && record.OrdersSnapshottedBeforeReset > 0)
                {
                    // Recovering from a zero — now we can decide
                    if (dto.Orders < record.OrdersSnapshottedBeforeReset)
                    {
                        // Real new shift (e.g. 10 → 0 → 3): commit today's old-shift contribution
                        record.OrdersBase += record.OrdersSnapshottedBeforeReset - record.OrdersDayStart;
                        record.OrdersDayStart = 0;   // baseline no longer applies
                    }
                    // else: glitch (10 → 0 → 10) — don't add anything

                    record.OrdersSnapshottedBeforeReset = 0;
                }

                record.LastSeenOrders = dto.Orders;

                // max(0,...) prevents a negative display during the zero-orders gap
                record.Orders = Math.Max(0, record.OrdersBase + dto.Orders - record.OrdersDayStart);

                // ── WALLET (unchanged) ───────────────────────────────────
                record.Wallet = dto.Wallet;
                record.LastUpdatedAt = now;
            }
            else
            {
                db.RiderStats.Add(new RiderStat
                {
                    RiderId = dto.RiderId,
                    RiderName = dto.RiderName,
                    CompanyId = dto.CompanyId,
                    Date = dto.Date,
                    Wallet = dto.Wallet,
                    Orders = 0,
                    OrdersBase = 0,
                    OrdersDayStart = dto.Orders,    // ← remember what API showed at day-start
                    LastSeenOrders = dto.Orders,
                    WorkingHours = dto.WorkingHours,
                    WorkingHoursBase = 0,
                    LastSeenWorkingHours = dto.WorkingHours,
                    LastUpdatedAt = now,
                    OrdersSnapshottedBeforeReset = 0,
                });
            }
            await db.SaveChangesAsync();
        }
    }

    // ── GET BY COMPANY + DATE ──────────────────────────────────────────────
    // WorkingHours and Orders in the DB are already the correct accumulated
    // totals — no extra calculation needed here.
    public async Task<CompanyDayStats?> GetByCompanyAndDateAsync(
        string companyId, DateOnly date)
    {
        var riders = await db.RiderStats
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Date == date)
            .OrderByDescending(r => r.WorkingHours)  
            .ToListAsync();

        if (!riders.Any()) return null;

        return BuildStats(companyId, date, riders);
    }

    // ── GET SUMMARY (last N days) ──────────────────────────────────────────
    public async Task<IEnumerable<CompanyDayStats>> GetCompanySummaryAsync(
        string companyId, int lastDays = 30)
    {
        var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-lastDays));

        var riders = await db.RiderStats
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Date >= from)
            .OrderByDescending(r => r.Date)
            .ToListAsync();

        return riders
            .GroupBy(r => r.Date)
            .Select(g => BuildStats(companyId, g.Key, g.ToList()));
    }

    // ── HELPER ─────────────────────────────────────────────────────────────
    // Reads WorkingHours and Orders directly from DB — they are already
    // the correct full-day totals thanks to the upsert logic above.
    private static CompanyDayStats BuildStats(
        string companyId, DateOnly date, IList<RiderStat> riders) =>
        new(
            companyId,
            date,
            TotalRiders: riders.Count,
            TotalOrders: riders.Sum(r => r.Orders),
            TotalWallet: riders.Sum(r => r.Wallet),
            TotalWorkingHours: riders.Sum(r => r.WorkingHours),
            Riders: riders.Select(r => new RiderStatDto(
                r.RiderId,
                r.RiderName,
                r.CompanyId,
                r.Date,
                r.Wallet,
                r.Orders,
                r.WorkingHours   // ← already the full-day accumulated total
            ))
        );
}