using LiveDahsboard.DTOs;
using LiveDahsboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace LiveDahsboard.Controllers.Api;

[ApiController]
[Route("api/rider-stats")]
public class RiderStatController(IRiderStatService service) : ControllerBase
{
    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] List<RiderStatDto> items)
    {
        if (items is null or { Count: 0 }) return BadRequest("Empty list");
        await service.UpsertBatchAsync(items);
        return NoContent();
    }

    [HttpGet("{companyId}/{date}")]
    public async Task<IActionResult> Get(string companyId, DateOnly date)
    {
        var result = await service.GetByCompanyAndDateAsync(companyId, date);
        return result is null ? NotFound() : Ok(result);
    }
}