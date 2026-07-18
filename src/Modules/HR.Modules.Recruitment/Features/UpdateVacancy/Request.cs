namespace HR.Modules.Recruitment.Features.UpdateVacancy;

internal sealed record UpdateVacancyRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    // Optional: when supplied and different from the vacancy's current PositionProfileId, the handler
    // attempts to change it — allowed only while the vacancy is Draft and has zero applications (see
    // UpdateVacancyHandler.CanChangePositionProfile). Null/unchanged means "leave as-is".
    public Guid? PositionProfileId { get; init; }
    public string? AdvertTitle { get; init; }
    public string? AdvertDescription { get; init; }
    public Guid HiringManagerId { get; init; }

    // Authorised correction escape hatch: when the baseline change-control check
    // (UpdateVacancyHandler.CanChangePositionProfile) would otherwise reject a Position Profile
    // change (vacancy published and/or has applications), setting this flag together with a
    // non-empty CorrectionReason allows the change to proceed anyway. The reason is mandatory
    // whenever this flag is set — an unexplained override defeats the point of the audit trail — see
    // UpdateVacancyValidator. Ignored entirely when PositionProfileId is unchanged.
    public bool IsAuthorisedCorrection { get; init; }
    public string? CorrectionReason { get; init; }
}
