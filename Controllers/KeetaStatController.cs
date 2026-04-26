using LiveDahsboard.DTOs;
using LiveDahsboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace LiveDahsboard.Controllers.Api;

[ApiController]
[Route("api/keeta-stats")]
public class KeetaStatController(IKeetaStatService service) : ControllerBase
{
    // PUT api/keeta-stats
    // Body: [ { courierId, courierName, orgId, date, ... }, ... ]
    [HttpPut]
    public async Task<IActionResult> Upsert([FromBody] List<KeetaStatDto> items)
    {
        if (items is null or { Count: 0 }) return BadRequest("Empty list");
        await service.UpsertBatchAsync(items);
        return NoContent();
    }

    // GET api/keeta-stats/{orgId}/{date}
    [HttpGet("{orgId}/{date}")]
    public async Task<IActionResult> Get(string orgId, DateOnly date)
    {
        var result = await service.GetByOrgAndDateAsync(orgId, date);
        return result is null ? NotFound() : Ok(result);
    }
}