using LiveDahsboard.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiveDahsboard.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<RiderStat> RiderStats => Set<RiderStat>();
    public DbSet<ExternalProvider> ExternalProviders => Set<ExternalProvider>();
    public DbSet<RiderNameOverride> RiderNameOverrides => Set<RiderNameOverride>();
    public DbSet<KeetaStat> KeetaStats => Set<KeetaStat>();

    public DbSet<RiderShiftStat> RiderShiftStats { get; set; }

    /// <summary>
    /// Append-only snapshot log — one row per rider per poll.
    /// Replaces RiderShiftStats.
    /// </summary>
    public DbSet<RiderSnapshot> RiderSnapshots { get; set; }

}