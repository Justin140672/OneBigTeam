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
    // Ticket #81: references ExternalRecruiter (an external agency) rather than an Employee — see
    // Vacancy.AssignedRecruiterId's remarks for the scope-correction history.
    Guid? AssignedRecruiterId,
    // Resolved agency display name for AssignedRecruiterId, so the Kanban card doesn't need to look
    // this up against the employee list anymore (it never was an employee) — null when unassigned or
    // (rare) the recruiter row can no longer be found.
    string? AssignedRecruiterAgencyName,
    string VacancyTitle);
