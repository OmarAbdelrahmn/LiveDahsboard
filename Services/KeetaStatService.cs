using LiveDahsboard.Data;
using LiveDahsboard.DTOs;
using LiveDahsboard.Models;
using Microsoft.EntityFrameworkCore;

namespace LiveDahsboard.Services;

public class KeetaStatService(ApplicationDbContext db) : IKeetaStatService
{
    public async Task UpsertBatchAsync(IEnumerable<KeetaStatDto> items)
    {
        var list = items.ToList();

        var dates = list.Select(i => i.Date).Distinct().ToList();
        var ids = list.Select(i => i.CourierId).Distinct().ToList();
        var orgIds = list.Select(i => i.OrgId).Distinct().ToList();

        var existing = await db.KeetaStats
            .Where(r => ids.Contains(r.CourierId)
                     && orgIds.Contains(r.OrgId)
                     && dates.Contains(r.Date))
            .ToListAsync();

        var map = existing.ToDictionary(r => (r.CourierId, r.OrgId, r.Date));
        var now = DateTime.UtcNow;

        foreach (var dto in list)
        {
            var key = (dto.CourierId, dto.OrgId, dto.Date);

            if (map.TryGetValue(key, out var record))
            {
                // Simple last-write-wins — no accumulation needed
                record.CourierName = dto.CourierName;
                record.FinishedTasks = dto.FinishedTasks;
                record.DeliveringTasks = dto.DeliveringTasks;
                record.CanceledTasks = dto.CanceledTasks;
                record.OnlineHours = dto.OnlineHours;
                record.StatusCode = dto.StatusCode;
                record.LastUpdatedAt = now;
            }
            else
            {
                var newRecord = new KeetaStat
                {
                    CourierId = dto.CourierId,
                    CourierName = dto.CourierName,
                    OrgId = dto.OrgId,
                    Date = dto.Date,
                    FinishedTasks = dto.FinishedTasks,
                    DeliveringTasks = dto.DeliveringTasks,
                    CanceledTasks = dto.CanceledTasks,
                    OnlineHours = dto.OnlineHours,
                    StatusCode = dto.StatusCode,
                    LastUpdatedAt = now,
                };
                db.KeetaStats.Add(newRecord);
                map[key] = newRecord;
            }
        }

        await db.SaveChangesAsync();
    }

    public async Task<KeetaDayStats?> GetByOrgAndDateAsync(string orgId, DateOnly date)
    {
        var couriers = await db.KeetaStats
            .AsNoTracking()
            .Where(r => r.OrgId == orgId && r.Date == date)
            .OrderByDescending(r => r.OnlineHours)
            .ToListAsync();

        if (!couriers.Any()) return null;

        return new KeetaDayStats(
            orgId,
            date,
            TotalCouriers: couriers.Count,
            TotalFinished: couriers.Sum(r => r.FinishedTasks),
            TotalDelivering: couriers.Sum(r => r.DeliveringTasks),
            TotalCanceled: couriers.Sum(r => r.CanceledTasks),
            TotalOnlineHours: couriers.Sum(r => r.OnlineHours),
            Couriers: couriers.Select(r => new KeetaStatDto(
                r.CourierId, r.CourierName, r.OrgId, r.Date,
                r.FinishedTasks, r.DeliveringTasks, r.CanceledTasks,
                r.OnlineHours, r.StatusCode
            ))
        );
    }
}