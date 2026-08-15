namespace HR.Modules.Companies.Features.GetCustomerBillingBreakdown;

internal sealed record GetCustomerBillingBreakdownResponse(
    Guid CompanyId,
    DateTimeOffset ComputedAt,
    int ActiveEmployees,
    int FutureStarters,
    int Leavers,
    int ChargeableEmployees,
    decimal PricePerEmployee,
    decimal Discounts,
    decimal MonthlyTotal,
    IReadOnlyList<BillingSnapshotDto> History);

internal sealed record BillingSnapshotDto(
    Guid Id,
    DateTimeOffset ComputedAt,
    int ActiveEmployees,
    int FutureStarters,
    int Leavers,
    int ChargeableEmployees,
    decimal PricePerEmployee,
    decimal Discounts,
    decimal MonthlyTotal);
