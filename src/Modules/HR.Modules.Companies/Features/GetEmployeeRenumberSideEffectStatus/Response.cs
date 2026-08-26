namespace HR.Modules.Companies.Features.GetEmployeeRenumberSideEffectStatus;

/// <summary>
/// SET-08: "the settings update response clearly reports whether the change is applied or
/// processing" — this endpoint lets a caller poll that same status afterwards, and "a failed
/// renumber operation is visible" — Status can be Pending/Processing/Processed/Failed, with
/// AttemptCount/ErrorMessage/FailedAt populated once a failure has occurred.
/// </summary>
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
