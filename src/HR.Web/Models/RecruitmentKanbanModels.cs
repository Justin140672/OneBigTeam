namespace HR.Web.Models;

// ── GET KANBAN ────────────────────────────────────────────────────────────────
// Mirrors HR.Modules.Recruitment.Features.GetRecruitmentKanban.Response — one column per
// ApplicationStatus (pipeline order), all eight always present including trailing
// Rejected/Withdrawn columns, even when empty.

public sealed record GetRecruitmentKanbanResponse(
    Guid VacancyId,
    string VacancyTitle,
    IReadOnlyList<KanbanColumnModel> Columns);

public sealed record KanbanColumnModel(
    string Stage,
    int Count,
    IReadOnlyList<KanbanApplicantModel> Applicants);

public sealed record KanbanApplicantModel(
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateFirstName,
    string CandidateLastName,
    // Always null today — Candidate has no photo field yet (see backend Handler comment). Card
    // template must render a placeholder avatar and not break on null.
    string? CandidatePhotoUrl,
    string Stage,
    DateTimeOffset AppliedAt,
    // Ticket #81: references ExternalRecruiter (an external agency), not an Employee — see the
    // backend Response's remarks for the scope-correction history.
    Guid? AssignedRecruiterId,
    // Resolved agency display name — server-resolved now, so this component no longer needs to look
    // it up against the employee list.
    string? AssignedRecruiterAgencyName,
    string VacancyTitle)
{
    public string CandidateFullName => $"{CandidateFirstName} {CandidateLastName}";
}

// ── MOVE STAGE ────────────────────────────────────────────────────────────────

public sealed record MoveApplicationStageRequest(
    Guid CompanyId,
    Guid VacancyId,
    Guid ApplicationId,
    string NewStatus,
    string? Notes = null);

public sealed record MoveApplicationStageResponse(
    Guid Id,
    Guid VacancyId,
    Guid CandidateId,
    string Status,
    string? InterviewOutcome,
    string? Notes,
    DateTimeOffset AppliedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
