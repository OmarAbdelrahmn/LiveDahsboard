using LiveDahsboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LiveDahsboard.Controllers;


[Authorize]
public class DashboardController(IRiderShiftStatService service) : Controller
{
    public async Task<IActionResult> Index(string companyId = "default", int days = 30)
    {
        var stats = await service.GetCompanySummaryAsync(companyId, days);
        ViewBag.CompanyId = companyId;
        ViewBag.Days = days;
        return View(stats);
    }

    public async Task<IActionResult> Day(string companyId, DateOnly date)
    {
        var stats = await service.GetByCompanyAndDateAsync(companyId, date);
        if (stats is null) return NotFound();
        return View(stats);
    }
}