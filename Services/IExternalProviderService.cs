using LiveDahsboard.Models;

namespace LiveDahsboard.Services;

public interface IExternalProviderService
{
    Task<ExternalProvider> CreateAsync(string username, DateTime startsAt, DateTime expiresAt);
    Task<bool?> IsValidAsync(string username);
    Task<IEnumerable<ExternalProvider>> GetAllAsync();
    Task DeleteAsync(int id);
}