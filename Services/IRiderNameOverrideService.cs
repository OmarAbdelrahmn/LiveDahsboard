using LiveDahsboard.DTOs;

namespace LiveDahsboard.Services;

public interface IRiderNameOverrideService
{
    /// <summary>
    /// Receives rider IDs for a specific company.
    /// Inserts any (riderId, companyId) pairs that don't exist yet.
    /// Never modifies existing rows.
    /// </summary>
    Task SyncRiderIdsAsync(string companyId, IEnumerable<string> riderIds);

    /// <summary>
    /// Returns every rider for a specific company with their override name.
    /// </summary>
    Task<IEnumerable<RiderNameOverrideDto>> GetAllAsync(string companyId);

    /// <summary>
    /// Sets the override name for a specific rider within a company.
    /// Returns false if the (riderId, companyId) pair was not found.
    /// </summary>
    Task<bool> UpdateNameAsync(string companyId, string riderId, string overrideName);

    /// Returns counts of inserted and updated records.
    /// </summary>
    Task<BulkUpsertResult> BulkUpsertNamesAsync(
        string companyId,
        IEnumerable<(string workingId, string name)> rows);
}

public record BulkUpsertResult(int Inserted, int Updated);
