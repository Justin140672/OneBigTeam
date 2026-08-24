namespace HR.Modules.Probation.Features.UpdateProbationRecord;

/// <summary>
/// PROB-05: administrative-correction fields only. Deliberately excludes Status, ExtensionReason,
/// DecisionMakerEmployeeId, DecisionDate and OutcomeNotes — those may only be set together,
/// consistently, by the proper review-completion/extension workflow
/// (CompleteProbationReview/CompleteProbationReviewFromTask), never by a direct record edit. See
/// <c>ProbationRecord.ApplyAdministrativeCorrection</c> for the full rationale.
/// </summary>
internal sealed record UpdateProbationRecordRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public Guid ManagerEmployeeId { get; init; }
    public DateOnly ExpectedEndDate { get; init; }
    public string? Notes { get; init; }
}
