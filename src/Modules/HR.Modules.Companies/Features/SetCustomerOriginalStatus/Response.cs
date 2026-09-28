namespace HR.Modules.Companies.Features.SetCustomerOriginalStatus;

internal sealed record SetCustomerOriginalStatusResponse(
    Guid CompanyId,
    bool IsOriginalCustomer,
    int Version);
