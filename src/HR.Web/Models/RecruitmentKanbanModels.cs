namespace HR.Web.Models;

// ── GET KANBAN ────────────────────────────────────────────────────────────────
// Mirrors HR.Modules.Recruitment.Features.GetRecruitmentKanban.Response (post ticket #99) — columns
// are now the company's own active RecruitmentStage rows, in DisplayOrder, not a fixed 8-status
// enum. There is no more dedicated "Withdrawn" column: a withdrawn application stays under its real
// current stage and is flagged via IsWithdrawn instead (see KanbanApplicantModel).

public sealed record GetRecruitmentKanbanResponse(
    Guid VacancyId,
    string VacancyTitle,
    IReadOnlyList<KanbanColumnModel> Columns);

public sealed record KanbanColumnModel(
    Guid StageId,
    string StageName,
    bool IsTerminal,
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
    Guid StageId,
    string StageName,
    // Ticket #99: a withdrawn application remains under its current stage rather than moving to a
    // dedicated column — this flag is orthogonal to StageId/StageName and drives a muted/greyed-out
    // card treatment regardless of which stage it's shown under.
    bool IsWithdrawn,
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

    // Syncfusion's Kanban card engine looks for a field literally named "Id" on the bound record to
    // track each card's identity internally (its own getting-started samples always include one) —
    // without it, drag-and-drop has nothing stable to key a dragged card back to once the drop
    // reflow happens, so drops silently fail to resolve even though the pointer sequence itself
    // looks fine. ApplicationId is the real per-card identity; this just exposes it under the name
    // the widget expects.
    public string Id => ApplicationId.ToString();

    // SfKanban's KeyField needs a string — StageId (Guid) is the real identity, this is purely a
    // rendering/wiring convenience for the Kanban column KeyField match.
    //
    // Must be a real settable property, not a computed one derived from StageId: on drag-and-drop,
    // SfKanban mutates the dropped card's bound KeyField property (StageKey) in place to match the
    // target column's key — it never touches StageId itself. A get-only `=> StageId.ToString()`
    // silently ate that mutation (nothing to set), so after a drop, args.Data.StageId in DragStop
    // was always still the *source* stage, making `moved.StageId == previous.StageId` true and
    // OnDragStopAsync return early without ever calling MoveApplicationStageAsync — the card
    // appeared to move (Syncfusion's own client-side reflow) but nothing was persisted, and the
    // next full reload snapped it back to its real (unchanged) stage. See OnDragStopAsync, which
    // reads the post-drop StageId back out of this property instead of the stale StageId field.
    public string StageKey { get; set; } = StageId.ToString();
}

// ── MOVE STAGE ────────────────────────────────────────────────────────────────
// Mirrors HR.Modules.Recruitment.Features.MoveApplicationStage (post ticket #99) — the target is
// now a RecruitmentStage id, not a fixed ApplicationStatus string.

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
