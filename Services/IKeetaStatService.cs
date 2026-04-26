using LiveDahsboard.DTOs;

namespace LiveDahsboard.Services;

public interface IKeetaStatService
{
    Task UpsertBatchAsync(IEnumerable<KeetaStatDto> items);
    Task<KeetaDayStats?> GetByOrgAndDateAsync(string orgId, DateOnly date);
}