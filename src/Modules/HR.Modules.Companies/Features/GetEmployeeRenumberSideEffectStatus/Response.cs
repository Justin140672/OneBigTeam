namespace HR.Modules.Companies.Features.GetEmployeeRenumberSideEffectStatus;

internal sealed record GetEmployeeRenumberSideEffectStatusResponse(
    Guid Id,
    Guid CompanyId,
    string Status,
    int AttemptCount,
    DateTimeOffset? LastAttemptAt,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? FailedAt);
