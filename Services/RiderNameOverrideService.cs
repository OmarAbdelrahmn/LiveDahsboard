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

    // ── ADD to RiderNameOverrideService ───────────────────────────────────────
    // File: Services/RiderNameOverrideService.cs

    public async Task<BulkUpsertResult> BulkUpsertNamesAsync(
        string companyId,
        IEnumerable<(string workingId, string name)> rows)
    {
        var list = rows.ToList();
        var riderIds = list.Select(r => r.workingId).Distinct().ToList();

        // Load all existing records for this company in ONE query
        var existing = await db.RiderNameOverrides
            .Where(r => r.CompanyId == companyId && riderIds.Contains(r.RiderId))
            .ToListAsync();

        var existingMap = existing.ToDictionary(r => r.RiderId);

        int inserted = 0, updated = 0;
        var now = DateTime.UtcNow;

        foreach (var (workingId, name) in list)
        {
            if (existingMap.TryGetValue(workingId, out var record))
            {
                // Row exists → update the override name
                record.OverrideName = name;
                record.UpdatedAt = now;
                updated++;
            }
            else
            {
                // Row does not exist → insert with name already set
                var newRecord = new RiderNameOverride
                {
                    RiderId = workingId,
                    CompanyId = companyId,
                    OverrideName = name,
                    CreatedAt = now
                };
                db.RiderNameOverrides.Add(newRecord);
                // Keep the local map consistent to handle duplicates in the same file
                existingMap[workingId] = newRecord;
                inserted++;
            }
        }

        await db.SaveChangesAsync();
        return new BulkUpsertResult(inserted, updated);
    }
}