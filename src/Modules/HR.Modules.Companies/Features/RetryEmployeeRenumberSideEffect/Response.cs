namespace HR.Modules.Companies.Features.RetryEmployeeRenumberSideEffect;

internal sealed record RetryEmployeeRenumberSideEffectResponse(
    Guid Id,
    Guid CompanyId,
    string Status);
