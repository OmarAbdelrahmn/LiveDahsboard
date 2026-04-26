
using LiveDahsboard.Data;
using LiveDahsboard.Models;
using LiveDahsboard.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// Cookie-based Identity for MVC — no AddApiEndpoints
builder.Services.AddIdentity<AppUser, IdentityRole>(o =>
{
    o.Password.RequireNonAlphanumeric = false;
    o.Password.RequiredLength = 6;
    o.SignIn.RequireConfirmedAccount = false;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/Login";
});

builder.Services.AddScoped<IRiderStatService, RiderStatService>();
builder.Services.AddScoped<IExternalProviderService, ExternalProviderService>();
builder.Services.AddScoped<IRiderNameOverrideService, RiderNameOverrideService>();
builder.Services.AddScoped<IKeetaStatService, KeetaStatService>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
