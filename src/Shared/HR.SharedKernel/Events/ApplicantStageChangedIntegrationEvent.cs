namespace HR.SharedKernel;

// PreviousStage/NewStage are plain strings (ApplicationStatus.ToString()) rather than the
// HR.Modules.Recruitment.Domain.ApplicationStatus enum: that enum is internal to the Recruitment
// module, and SharedKernel must not contain module-specific enums (see
// specifications/architecture/01-solution-structure.md's "Forbidden contents" for SharedKernel) nor
// may a consuming module reference Recruitment's internal types directly. ApplicantId identifies the
// Application aggregate (this module has no separate "Applicant" entity — an Application already
// represents one candidate's application to one vacancy).
public sealed record ApplicantStageChangedIntegrationEvent(
    Guid CompanyId,
    Guid ApplicantId,
    Guid VacancyId,
    string PreviousStage,
    string NewStage,
    Guid ChangedBy,
    DateTimeOffset ChangedDate) : IIntegrationEvent;
