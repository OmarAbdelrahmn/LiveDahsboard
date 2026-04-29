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

// DTOs/RiderShiftStatIncoming.cs
// This is what the frontend PUT sends — raw values, no derived fields.
public record RiderShiftStatIncoming(
    string RiderId,
    string RiderName,
    string CompanyId,
    string Status,
    DateTime? ActiveShiftStartedAt,
    DateTime? ActiveShiftEndedAt,
    decimal Wallet,
    int Orders,
    int WorkedSeconds,      // raw from performance.time_spent.worked_seconds
    int BreakSeconds        // raw from performance.time_spent.break_seconds
);

public record RiderShiftStatDto(
    string RiderId,
    string RiderName,
    string CompanyId,
    DateTime ActiveShiftStartedAt,
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
    IEnumerable<RiderShiftStatDto> Riders   // was RiderStatDto
);



public record KeetaStatDto(
    string CourierId,
    string CourierName,
    string OrgId,
    DateOnly Date,
    int FinishedTasks,
    int DeliveringTasks,
    int CanceledTasks,
    decimal OnlineHours,
    int StatusCode          // raw Keeta status: 20=going, 30=delivering, 40=offline
);

public record KeetaDayStats(
    string OrgId,
    DateOnly Date,
    int TotalCouriers,
    int TotalFinished,
    int TotalDelivering,
    int TotalCanceled,
    decimal TotalOnlineHours,
    IEnumerable<KeetaStatDto> Couriers
);