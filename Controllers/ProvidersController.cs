using LiveDahsboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LiveDahsboard.Controllers;


[Authorize]
public class ProvidersController(IExternalProviderService service) : Controller
{
    public async Task<IActionResult> Index(string companyId = "default")
    {
        var providers = await service.GetByCompanyAsync(companyId);
        ViewBag.CompanyId = companyId;
        return View(providers);
    }

    [HttpGet]
    public IActionResult Create() => View();

    [HttpPost]
    public async Task<IActionResult> Create(string companyId, string username,
        DateTime startsAt, DateTime expiresAt)
    {
        await service.CreateAsync(companyId, username, startsAt, expiresAt);
        return RedirectToAction(nameof(Index), new { companyId });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, string companyId)
    {
        await service.DeleteAsync(id);
        return RedirectToAction(nameof(Index), new { companyId });
    }
}
