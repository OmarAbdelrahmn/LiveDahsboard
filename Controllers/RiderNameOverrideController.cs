using LiveDahsboard.DTOs;
using LiveDahsboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace LiveDahsboard.Controllers.Api;

[ApiController]
[Route("api/rider-names/{companyId}")]
public class RiderNameOverrideController(IRiderNameOverrideService service) : ControllerBase
{
    // POST api/rider-names/{companyId}/sync
    // Body: ["rider-001", "rider-002", ...]
    [HttpPost("sync")]
    public async Task<IActionResult> Sync(string companyId, [FromBody] List<string> riderIds)
    {
        if (riderIds is null or { Count: 0 })
            return BadRequest("Rider ID list is empty.");

        await service.SyncRiderIdsAsync(companyId, riderIds);
        return NoContent();
    }

    // GET api/rider-names/{companyId}
    // Returns all riders for the company with their override name (null if not set)
    [HttpGet]
    public async Task<IActionResult> GetAll(string companyId)
    {
        var result = await service.GetAllAsync(companyId);
        return Ok(result);
    }

    // PATCH api/rider-names/{companyId}/{riderId}
    // Body: { "overrideName": "Ahmed Al-Ghamdi" }
    [HttpPatch("{riderId}")]
    public async Task<IActionResult> UpdateName(string companyId, string riderId,
        [FromBody] UpdateRiderNameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OverrideName))
            return BadRequest("Override name cannot be empty.");

        var found = await service.UpdateNameAsync(companyId, riderId, request.OverrideName);
        return found ? NoContent() : NotFound(new { error = "Rider not found for this company." });
    }
}