namespace HR.Modules.Recruitment.Features.GetRecruitmentKanban;

internal sealed record GetRecruitmentKanbanResponse(
    Guid VacancyId,
    string VacancyTitle,
    IReadOnlyList<KanbanColumn> Columns);

// Ticket #99: StageId/StageName replace the fixed ApplicationStatus enum value — columns are now the
// company's own active RecruitmentStage rows in DisplayOrder (see GetRecruitmentKanbanHandler).
internal sealed record KanbanColumn(
    Guid StageId,
    string StageName,
    bool IsTerminal,
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
    Guid StageId,
    string StageName,
    // Ticket #99: surfaced so the UI can visually distinguish/exclude withdrawn applications without
    // a separate column — a withdrawn application still appears under its CurrentStageId column (see
    // Application.WithdrawnAt's remarks) rather than moving to a dedicated "Withdrawn" column, since
    // no such stage exists.
    bool IsWithdrawn,
    DateTimeOffset AppliedAt,
    // Ticket #81: references ExternalRecruiter (an external agency) rather than an Employee — see
    // Vacancy.AssignedRecruiterId's remarks for the scope-correction history.
    Guid? AssignedRecruiterId,
    // Resolved agency display name for AssignedRecruiterId, so the Kanban card doesn't need to look
    // this up against the employee list anymore (it never was an employee) — null when unassigned or
    // (rare) the recruiter row can no longer be found.
    string? AssignedRecruiterAgencyName,
    string VacancyTitle);
