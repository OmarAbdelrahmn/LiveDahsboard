using ClosedXML.Excel;
using LiveDahsboard.DTOs;
using LiveDahsboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace LiveDahsboard.Controllers;

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
    [HttpPut("{riderId}")]
    public async Task<IActionResult> UpdateName(string companyId, string riderId,
        [FromBody] UpdateRiderNameRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OverrideName))
            return BadRequest("Override name cannot be empty.");

        var found = await service.UpdateNameAsync(companyId, riderId, request.OverrideName);
        return found ? NoContent() : NotFound(new { error = "Rider not found for this company." });
    }

    [HttpPost("import")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportFromExcel(
       string companyId,
       IFormFile file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded." });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".xlsx")
            return BadRequest(new { error = "Only .xlsx files are accepted." });

        // ── Parse the workbook ─────────────────────────────────────────
        var rows = new List<(string workingId, string name)>();

        using (var stream = file.OpenReadStream())
        using (var wb = new XLWorkbook(stream))
        {
            var ws = wb.Worksheets.First();

            // Skip header row (row 1), start from row 2
            foreach (var row in ws.RowsUsed().Skip(1))
            {
                var workingId = row.Cell(1).GetString().Trim();
                var name = row.Cell(2).GetString().Trim();

                if (string.IsNullOrWhiteSpace(workingId) ||
                    string.IsNullOrWhiteSpace(name))
                    continue;   // skip blank / incomplete rows

                rows.Add((workingId, name));
            }
        }

        if (rows.Count == 0)
            return BadRequest(new { error = "Excel file contains no valid data rows." });

        // ── Delegate to service ────────────────────────────────────────
        var result = await service.BulkUpsertNamesAsync(companyId, rows);

        return Ok(new
        {
            processed = rows.Count,
            inserted = result.Inserted,
            updated = result.Updated
        });
    }
}