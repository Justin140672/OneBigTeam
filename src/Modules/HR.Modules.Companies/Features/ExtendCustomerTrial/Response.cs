namespace HR.Modules.Companies.Features.ExtendCustomerTrial;

internal sealed record ExtendCustomerTrialResponse(
    Guid CompanyId,
    string Status,
    DateTimeOffset TrialExpiresAt);
