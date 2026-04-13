using LiveDahsboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LiveDahsboard.Controllers;

[Authorize]
public class ProvidersController(IExternalProviderService service) : Controller
{
    public async Task<IActionResult> Index()
    {
        var providers = await service.GetAllAsync();
        return View(providers);
    }

    [HttpGet]
    public IActionResult Create() => View();

    [HttpPost]
    public async Task<IActionResult> Create(string username, DateTime startsAt, DateTime expiresAt)
    {
        await service.CreateAsync(username, startsAt, expiresAt);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        await service.DeleteAsync(id);
        return RedirectToAction(nameof(Index));
    }
}

