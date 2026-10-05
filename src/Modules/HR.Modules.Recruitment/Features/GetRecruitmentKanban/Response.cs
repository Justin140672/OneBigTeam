namespace HR.Modules.Recruitment.Features.GetRecruitmentKanban;

internal sealed record GetRecruitmentKanbanResponse(
    Guid VacancyId,
    string VacancyTitle,
    IReadOnlyList<KanbanColumn> Columns);

internal sealed record KanbanColumn(
    Guid StageId,
    string StageName,
    bool IsTerminal,
    int Count,
    IReadOnlyList<KanbanCandidateSummary> Candidates,
    Domain.RecruitmentStagePurpose? Purpose = null);

internal sealed record KanbanCandidateSummary(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    string? CandidatePhotoUrl,
    Guid StageId,
    string StageName,
    bool IsWithdrawn,
    DateTimeOffset AppliedAt,
    // Ticket #81: references ExternalRecruiter (an external agency) rather than an Employee — see
    // Vacancy.AssignedRecruiterId's remarks for the scope-correction history.
    Guid? AssignedRecruiterId,
    string? AssignedRecruiterAgencyName,
    string VacancyTitle,
    // Internal recruitment Ticket 6: true only when the application's Source == Internal. Internal
    // applications share the same configured stage columns as external ones. EmployeeId is populated
    // only for internal applications.
    bool IsInternal = false,
    Guid? EmployeeId = null,
    string? InterviewOutcome = null,
    string? OfferResponseStatus = null,
    decimal? OfferedSalary = null,
    DateOnly? OfferedStartDate = null,
    bool CurrentStageHasPendingInterview = false,
    Guid? PendingInterviewId = null,
    string? CurrentStageInterviewOutcome = null,
    bool HasNextInterviewStage = false,
    Guid? NextInterviewStageId = null,
    string? NextInterviewStageName = null,
    string? InternalAppointmentStatus = null,
    bool AllRequiredInterviewStagesPassed = true);
