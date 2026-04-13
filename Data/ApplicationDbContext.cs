using LiveDahsboard.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiveDahsboard.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<RiderStat> RiderStats => Set<RiderStat>();
    public DbSet<ExternalProvider> ExternalProviders => Set<ExternalProvider>();
}