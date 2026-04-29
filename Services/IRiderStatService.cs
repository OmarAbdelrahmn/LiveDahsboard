using LiveDahsboard.DTOs;

namespace LiveDahsboard.Services;

// Services/IRiderShiftStatService.cs
public interface IRiderShiftStatService
{
    /// <summary>
    /// Upserts a batch of shift snapshots.
    /// Key: (RiderId, CompanyId, ActiveShiftStartedAt)
    /// </summary>
    Task UpsertBatchAsync(IEnumerable<RiderShiftStatIncoming> items);

    /// <summary>
    /// Returns aggregated totals + all shifts for a company on a given date.
    /// A rider with 2 shifts on the same day will appear as 2 rows.
    /// </summary>
    Task<CompanyDayStats?> GetByCompanyAndDateAsync(string companyId, DateOnly date);

    /// <summary>
    /// Returns day-level summaries for the last N days.
    /// </summary>
    Task<IEnumerable<CompanyDayStats>> GetCompanySummaryAsync(string companyId, int lastDays = 30);
}