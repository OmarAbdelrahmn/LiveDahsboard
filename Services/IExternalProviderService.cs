using LiveDahsboard.Models;

namespace LiveDahsboard.Services;

public interface IExternalProviderService
{
    Task<ExternalProvider> CreateAsync(string companyId, string username, DateTime startsAt, DateTime expiresAt);
    Task<bool?> IsValidAsync(string username);   // null = not found
    Task<IEnumerable<ExternalProvider>> GetByCompanyAsync(string companyId);
    Task DeleteAsync(int id);
}