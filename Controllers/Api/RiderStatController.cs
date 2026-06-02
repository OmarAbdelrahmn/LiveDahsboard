using LiveDahsboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace LiveDahsboard.Controllers.Api;

[ApiController]
[Route("api/rider-stats")]
public class RiderStatController(IRiderSnapshotService service) : ControllerBase
{
    // ── PUT /api/rider-stats ─────────────────────────────────────────────────
    // Kept as PUT (same verb the front-end already uses).
    // Body shape is identical to the old RiderShiftStatIncoming —
    // RiderSnapshotIncoming just renames WorkedSeconds (was raw seconds before too).
    [HttpPut]
    public async Task<IActionResult> Insert([FromBody] List<RiderSnapshotIncoming> items)
    {
        if (items is null or { Count: 0 }) return BadRequest("Empty list");
        await service.InsertBatchAsync(items);
        return NoContent();
    }

    // ── GET /api/rider-stats/{companyId}/{date} ───────────────────────────────
    // Route, params, and response shape are unchanged.
    [HttpGet("{companyId}/{date}")]
    public async Task<IActionResult> Get(string companyId, DateOnly date)
    {
        var result = await service.GetByCompanyAndDateAsync(companyId, date);
        return result is null ? NotFound() : Ok(result);
    }
}