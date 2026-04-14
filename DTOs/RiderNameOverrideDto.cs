namespace LiveDahsboard.DTOs;

public record RiderNameOverrideDto(string RiderId, string CompanyId, string? OverrideName);

public record UpdateRiderNameRequest(string OverrideName);