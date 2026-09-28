namespace HR.Modules.Companies.Features.SetCustomerOriginalStatus;

internal sealed record SetCustomerOriginalStatusRequest(
    bool IsOriginalCustomer,
    int ExpectedVersion);
