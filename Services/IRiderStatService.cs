using LiveDahsboard.DTOs;

namespace LiveDahsboard.Services;

public interface IRiderStatService
{
    Task UpsertBatchAsync(IEnumerable<RiderStatDto> items);
    Task<CompanyDayStats?> GetByCompanyAndDateAsync(string companyId, DateOnly date);
    Task<IEnumerable<CompanyDayStats>> GetCompanySummaryAsync(string companyId, int lastDays = 30);
}