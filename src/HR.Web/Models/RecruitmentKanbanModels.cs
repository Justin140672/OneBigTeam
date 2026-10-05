namespace HR.Web.Models;


public sealed record GetRecruitmentKanbanResponse(
    Guid VacancyId,
    string VacancyTitle,
    IReadOnlyList<KanbanColumnModel> Columns);

public sealed record KanbanColumnModel(
    Guid StageId,
    string StageName,
    bool IsTerminal,
    int Count,
    IReadOnlyList<KanbanCandidateModel> Candidates,
    RecruitmentStagePurpose? Purpose = null);

public sealed record KanbanCandidateModel(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    string? CandidatePhotoUrl,
    Guid StageId,
    string StageName,
    bool IsWithdrawn,
    DateTimeOffset AppliedAt,
    // Ticket #81: references ExternalRecruiter (an external agency), not an Employee — see the
    // backend Response's remarks for the scope-correction history.
    Guid? AssignedRecruiterId,
    string? AssignedRecruiterAgencyName,
    string VacancyTitle,
    // Internal recruitment Ticket 6: true only when the application's Source == Internal (never
    // inferred from EmployeeId). EmployeeId is populated only for internal applications.
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
    bool AllRequiredInterviewStagesPassed = true) : IKanbanActionSource
{
    public string CandidateFullName => $"{CandidateFirstName} {CandidateLastName}";
}


public sealed record MoveApplicationStageRequest(
    Guid CompanyId,
    Guid VacancyId,
    Guid ApplicationId,
    Guid NewStageId,
    string? Notes = null);

public sealed record MoveApplicationStageResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    Guid CurrentStageId,
    string? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
