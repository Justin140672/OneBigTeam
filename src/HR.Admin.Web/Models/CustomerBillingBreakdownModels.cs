namespace HR.Admin.Web.Models;

// Mirrors HR.Modules.Companies.Features.GetCustomerBillingBreakdown.Response exactly — same
// "app-local DTO matching the API contract" convention as CustomerDetailsModels.cs.
public sealed record CustomerBillingBreakdownResponse(
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

public sealed record BillingSnapshotDto(
    Guid Id,
    DateTimeOffset ComputedAt,
    int ActiveEmployees,
    int FutureStarters,
    int Leavers,
    int ChargeableEmployees,
    decimal PricePerEmployee,
    decimal Discounts,
    decimal MonthlyTotal);
