using LiveDahsboard.Data;
using LiveDahsboard.DTOs;
using LiveDahsboard.Models;
using Microsoft.EntityFrameworkCore;

namespace LiveDahsboard.Services;

public class RiderNameOverrideService(ApplicationDbContext db) : IRiderNameOverrideService
{
    public async Task SyncRiderIdsAsync(string companyId, IEnumerable<string> riderIds)
    {
        var incoming = riderIds.Distinct().ToList();

        // Load only the rider IDs that already exist for this company — one query
        var existing = await db.RiderNameOverrides
            .Where(r => r.CompanyId == companyId && incoming.Contains(r.RiderId))
            .Select(r => r.RiderId)
            .ToHashSetAsync();

        var toAdd = incoming
            .Where(id => !existing.Contains(id))
            .Select(id => new RiderNameOverride
            {
                RiderId = id,
                CompanyId = companyId
            });

        db.RiderNameOverrides.AddRange(toAdd);
        await db.SaveChangesAsync();
    }

    public async Task<IEnumerable<RiderNameOverrideDto>> GetAllAsync(string companyId) =>
        await db.RiderNameOverrides
            .AsNoTracking()
            .Where(r => r.CompanyId == companyId)
            .OrderBy(r => r.RiderId)
            .Select(r => new RiderNameOverrideDto(r.RiderId, r.CompanyId, r.OverrideName))
            .ToListAsync();

    public async Task<bool> UpdateNameAsync(string companyId, string riderId, string overrideName)
    {
        var record = await db.RiderNameOverrides
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.RiderId == riderId);

        if (record is null) return false;

        record.OverrideName = overrideName;
        record.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }
}