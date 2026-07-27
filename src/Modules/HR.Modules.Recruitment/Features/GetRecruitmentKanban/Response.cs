using HR.Modules.Recruitment.Domain;

namespace HR.Modules.Recruitment.Features.GetRecruitmentKanban;

internal sealed record GetRecruitmentKanbanResponse(
    Guid VacancyId,
    string VacancyTitle,
    IReadOnlyList<KanbanColumn> Columns);

internal sealed record KanbanColumn(
    ApplicationStatus Stage,
    int Count,
    IReadOnlyList<KanbanApplicantSummary> Applicants);

internal sealed record KanbanApplicantSummary(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    // Candidate has no photo/avatar field in this codebase today (see Domain/Candidate.cs — only
    // ResumeUrl exists) — left null pending a future Candidate.PhotoUrl field, so the Kanban card
    // contract already has a slot for it without a breaking change later.
    string? CandidatePhotoUrl,
    ApplicationStatus Stage,
    DateTimeOffset AppliedAt,
    Guid? AssignedRecruiterId,
    string VacancyTitle);
