using LiveDahsboard.Data;
using LiveDahsboard.DTOs;
using LiveDahsboard.Models;
using Microsoft.EntityFrameworkCore;

namespace LiveDahsboard.Services;


public class RiderStatService(ApplicationDbContext db) : IRiderStatService
{
    public async Task UpsertBatchAsync(IEnumerable<RiderStatDto> items)
    {
        var list = items.ToList();

        // Load all existing matching records in ONE query
        var keys = list.Select(i => new { i.RiderId, i.RiderName, i.Date, i.CompanyId }).ToList();
        var dates = keys.Select(k => k.Date).Distinct().ToList();
        var riderIds = keys.Select(k => k.RiderId).Distinct().ToList();
        var companyIds = keys.Select(k => k.CompanyId).Distinct().ToList();

        var existing = await db.RiderStats
            .Where(r => riderIds.Contains(r.RiderId)
                     && companyIds.Contains(r.CompanyId)
                     && dates.Contains(r.Date))
            .ToListAsync();

        var existingMap = existing.ToDictionary(
            r => (r.RiderId, r.RiderName, r.Date, r.CompanyId));

        var now = DateTime.UtcNow;

        foreach (var dto in list)
        {
            var key = (dto.RiderId, dto.RiderName, dto.Date, dto.CompanyId);
            if (existingMap.TryGetValue(key, out var record))
            {
                record.Wallet = dto.Wallet;
                record.Orders = dto.Orders;
                record.WorkingHours = dto.WorkingHours;
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
                    Orders = dto.Orders,
                    WorkingHours = dto.WorkingHours,
                    LastUpdatedAt = now
                });
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<CompanyDayStats?> GetByCompanyAndDateAsync(string companyId, DateOnly date)
    {
        var riders = await db.RiderStats
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId && r.Date == date)
            .ToListAsync();

        if (!riders.Any()) return null;

        return BuildStats(companyId, date, riders);
    }

    public async Task<IEnumerable<CompanyDayStats>> GetCompanySummaryAsync(string companyId, int lastDays = 30)
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

    private static CompanyDayStats BuildStats(string companyId, DateOnly date, IList<RiderStat> riders) =>
        new(
            companyId, date,
            riders.Count,
            riders.Sum(r => r.Orders),
            riders.Sum(r => r.Wallet),
            riders.Sum(r => r.WorkingHours),
            riders.Select(r => new RiderStatDto(r.RiderId, r.RiderName, r.CompanyId, r.Date, r.Wallet, r.Orders, r.WorkingHours))
        );
}