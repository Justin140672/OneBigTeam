namespace HR.Modules.Companies.Features.ListCustomers;

internal sealed record ListCustomersResponse(IReadOnlyList<CustomerListItemDto> Customers);

internal sealed record CustomerListItemDto(
    Guid CompanyId,
    string CompanyName,
    string SubscriptionStatus,
    int CurrentEmployeeCount,
    decimal? MonthlyCharge,
    DateTimeOffset? TrialEndsAt,
    DateTimeOffset CreatedAt);
