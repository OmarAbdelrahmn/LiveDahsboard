namespace LiveDahsboard.DTOs;

public record RiderStatDto(
    string RiderId,
    string RiderName,
    string CompanyId,
    DateOnly Date,
    decimal Wallet,
    int Orders,
    decimal WorkingHours
);

public record CompanyDayStats(
    string CompanyId,
    DateOnly Date,
    int TotalRiders,
    int TotalOrders,
    decimal TotalWallet,
    decimal TotalWorkingHours,
    IEnumerable<RiderStatDto> Riders
);