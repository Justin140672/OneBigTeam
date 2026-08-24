namespace HR.Modules.Probation.Features.MarkProbationNotApplicable;

internal sealed record MarkProbationNotApplicableResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    string Status,
    string? NotApplicableReason,
    DateTimeOffset UpdatedAt);
