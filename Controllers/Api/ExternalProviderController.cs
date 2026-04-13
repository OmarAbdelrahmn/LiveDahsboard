using LiveDahsboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace LiveDahsboard.Controllers.Api;

[ApiController]
[Route("api/providers")]
public class ExternalProviderController(IExternalProviderService service) : ControllerBase
{
    [HttpGet("validate/{username}")]
    public async Task<IActionResult> Validate(string username)
    {
        var result = await service.IsValidAsync(username);
        if (result is null) return NotFound(new { valid = false, reason = "not_found" });
        return Ok(new { valid = result });
    }
}