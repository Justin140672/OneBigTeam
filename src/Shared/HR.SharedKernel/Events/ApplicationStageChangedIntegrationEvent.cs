namespace HR.SharedKernel;

// PreviousStage/NewStage are plain strings (ApplicationStatus.ToString()) rather than the
// HR.Modules.Recruitment.Domain.ApplicationStatus enum: that enum is internal to the Recruitment
// module, and SharedKernel must not contain module-specific enums (see
// specifications/architecture/01-solution-structure.md's "Forbidden contents" for SharedKernel) nor
// may a consuming module reference Recruitment's internal types directly. ApplicationId identifies the
// Application aggregate — the stage belongs to the Application (one candidate's application to one
// vacancy), not to the candidate as a person.
public sealed record ApplicationStageChangedIntegrationEvent(
    Guid CompanyId,
    Guid ApplicationId,
    Guid VacancyId,
    string PreviousStage,
    string NewStage,
    Guid ChangedBy,
    DateTimeOffset ChangedDate) : IIntegrationEvent;
