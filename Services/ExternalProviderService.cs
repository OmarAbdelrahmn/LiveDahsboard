using LiveDahsboard.Data;
using LiveDahsboard.Models;
using Microsoft.EntityFrameworkCore;

namespace LiveDahsboard.Services;


public class ExternalProviderService(ApplicationDbContext db) : IExternalProviderService
{
    public async Task<ExternalProvider> CreateAsync(string username, DateTime startsAt, DateTime expiresAt)
    {
        var provider = new ExternalProvider { Username = username, StartsAt = startsAt, ExpiresAt = expiresAt };
        db.ExternalProviders.Add(provider);
        await db.SaveChangesAsync();
        return provider;
    }

    public async Task<bool?> IsValidAsync(string username)
    {
        var p = await db.ExternalProviders.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Username == username);
        return p is null ? null : p.IsValid;
    }

    public async Task<IEnumerable<ExternalProvider>> GetAllAsync() =>
        await db.ExternalProviders.AsNoTracking()
                .OrderByDescending(p => p.ExpiresAt)
                .ToListAsync();

    public async Task DeleteAsync(int id) =>
        await db.ExternalProviders.Where(p => p.Id == id).ExecuteDeleteAsync();
}