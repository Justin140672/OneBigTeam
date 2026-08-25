using HR.SharedKernel;

namespace HR.Modules.Probation;

/// <summary>
/// PROB-07: shared "system" actor sentinel for probation audit events that originate from a
/// background/automated process rather than an authenticated human action — e.g. deferred
/// probation-record creation on employee hire (<see cref="Features.CreateProbationOnEmployeeCreated.EmployeeCreatedHandler"/>,
/// <see cref="Features.ReassignReviewsOnManagerChanged.ManagerChangedHandler"/>) and scheduled
/// review generation (<see cref="Jobs.GenerateDueProbationReviewsJob"/>). Mirrors the
/// SystemActorId convention already used elsewhere for job-originated audit events (see
/// HR.Modules.Leave.Jobs.LeaveYearRolloverService.SystemActorId,
/// HR.Modules.Sickness.Services.FitNoteEvidenceRequestService.SystemActorId). Never used for a
/// review/record created directly by an authenticated user via the API.
/// </summary>
internal static class ProbationSystemActor
{
    public static readonly Guid Id = Guid.Empty;
}

/// <summary>
/// PROB-07: ActorEmployeeIdValue distinguishes who caused the record to exist — the authenticated
/// caller for a direct CreateProbationRecord API call, or <see cref="ProbationSystemActor.Id"/> for
/// system-originated creation (employee hire, deferred manager-assignment completion). Notes is
/// deliberately never carried into the audit payload — only a presence flag (HasNotes) is recorded,
/// consistent with the platform-wide rule that free-text content must not appear in general audit
/// payloads.
/// </summary>
internal sealed record ProbationRecordCreatedAuditEvent(
    Guid CompanyId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    Guid ManagerEmployeeId,
    Guid? ActorEmployeeIdValue,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    bool HasNotes,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "probation-record.created";
    string IAuditEvent.EntityType       => "ProbationRecord";
    Guid   IAuditEvent.EntityId         => ProbationRecordId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => ActorEmployeeIdValue;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => ActorEmployeeIdValue == ProbationSystemActor.Id
        ? "Probation record created automatically on hire"
        : "Probation record created";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { EmployeeId, ManagerEmployeeId, StartDate, ExpectedEndDate, HasNotes };
    object? IAuditEvent.Metadata        => null;
}

/// <summary>
/// PROB-07: Reason is a free-text field and is deliberately excluded from the payload — only a
/// presence flag (HasReason) is recorded. Actor is the authenticated caller who made the explicit
/// "does not apply" decision (this action has no system-generated path).
/// </summary>
internal sealed record ProbationMarkedNotApplicableAuditEvent(
    Guid CompanyId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    Guid? ActorEmployeeIdValue,
    bool HasReason,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "probation-record.marked-not-applicable";
    string IAuditEvent.EntityType       => "ProbationRecord";
    Guid   IAuditEvent.EntityId         => ProbationRecordId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => ActorEmployeeIdValue;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => "Probation marked not applicable";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { Status = "NotApplicable", HasReason };
    object? IAuditEvent.Metadata        => null;
}

/// <summary>
/// PROB-07: administrative corrections only (PROB-05 restricted UpdateProbationRecord to
/// Manager/ExpectedEndDate/Notes-adjacent fields) — Before/After deliberately carry only the
/// structured, non-sensitive fields that can actually change (manager, expected end date); Notes
/// content itself is never included, only a presence flag. Actor is always the authenticated caller
/// — there is no system-generated path for this action.
/// </summary>
internal sealed record ProbationRecordUpdatedAuditEvent(
    Guid CompanyId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    Guid? ActorEmployeeIdValue,
    Guid BeforeManagerEmployeeId,
    DateOnly BeforeExpectedEndDate,
    Guid ManagerEmployeeId,
    DateOnly ExpectedEndDate,
    bool HasNotes,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "probation-record.updated";
    string IAuditEvent.EntityType       => "ProbationRecord";
    Guid   IAuditEvent.EntityId         => ProbationRecordId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => ActorEmployeeIdValue;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => "Probation record updated";
    object? IAuditEvent.Before          => new { ManagerEmployeeId = BeforeManagerEmployeeId, ExpectedEndDate = BeforeExpectedEndDate };
    object? IAuditEvent.After           => new { ManagerEmployeeId, ExpectedEndDate, HasNotes };
    object? IAuditEvent.Metadata        => null;
}

/// <summary>
/// PROB-07: ActorEmployeeIdValue distinguishes a review created directly by a human via
/// CreateProbationReview (the authenticated caller) from one created automatically by
/// GenerateDueProbationReviewsJob (<see cref="ProbationSystemActor.Id"/>).
/// </summary>
internal sealed record ProbationReviewCreatedAuditEvent(
    Guid CompanyId,
    Guid ProbationReviewId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    Guid? ActorEmployeeIdValue,
    string ReviewType,
    DateOnly DueDate,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "probation-review.created";
    string IAuditEvent.EntityType       => "ProbationReview";
    Guid   IAuditEvent.EntityId         => ProbationReviewId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => ActorEmployeeIdValue;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => ActorEmployeeIdValue == ProbationSystemActor.Id
        ? $"{ReviewType} review created automatically"
        : $"{ReviewType} review created";
    object? IAuditEvent.Before          => null;
    object? IAuditEvent.After           => new { ProbationRecordId, ReviewType, DueDate };
    object? IAuditEvent.Metadata        => null;
}

/// <summary>
/// PROB-07: ExtensionReason is free-text and is excluded from the payload — only a presence flag
/// (HasExtensionReason) is recorded; the structured before/after dates carry the meaningful,
/// non-sensitive change. Actor is always the human decision maker who completed the review that
/// caused the extension (there is no system-generated extension path).
/// </summary>
internal sealed record ProbationExtendedAuditEvent(
    Guid CompanyId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    Guid DecisionMakerEmployeeId,
    DateOnly PreviousExpectedEndDate,
    DateOnly NewExpectedEndDate,
    bool HasExtensionReason,
    DateOnly DecisionDate,
    Guid ExtensionConfirmationReviewId,
    Guid NewFinalReviewId,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "probation-record.extended";
    string IAuditEvent.EntityType       => "ProbationRecord";
    Guid   IAuditEvent.EntityId         => ProbationRecordId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => DecisionMakerEmployeeId;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"Probation extended to {NewExpectedEndDate:d MMM yyyy}";
    object? IAuditEvent.Before          => new { ExpectedEndDate = PreviousExpectedEndDate };
    object? IAuditEvent.After           => new { ExpectedEndDate = NewExpectedEndDate, HasExtensionReason, DecisionDate };
    object? IAuditEvent.Metadata        => new { ExtensionConfirmationReviewId, NewFinalReviewId };
}

/// <summary>
/// PROB-07: used only for review completions that carry no pass/fail/extend outcome (ManagerCheckIn
/// and HrReview checkpoint reviews). Pass, Fail and Extend are each recorded as their own distinct,
/// clearly-typed business event (<see cref="ProbationPassedAuditEvent"/>,
/// <see cref="ProbationFailedAuditEvent"/>, <see cref="ProbationExtendedAuditEvent"/>) rather than
/// this generic "completed" event, so an audit consumer can filter directly on the outcome that
/// occurred instead of parsing a free-form Outcome string. Notes is free-text and is excluded from
/// the payload — only a presence flag is recorded.
/// </summary>
internal sealed record ProbationReviewCompletedAuditEvent(
    Guid CompanyId,
    Guid ProbationReviewId,
    Guid ProbationRecordId,
    Guid EmployeeId,
    Guid CompletedByEmployeeId,
    string ReviewType,
    bool HasNotes,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "probation-review.completed";
    string IAuditEvent.EntityType       => "ProbationReview";
    Guid   IAuditEvent.EntityId         => ProbationReviewId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => CompletedByEmployeeId;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => $"{ReviewType} review completed";
    object? IAuditEvent.Before          => new { Status = "Pending" };
    object? IAuditEvent.After           => new { Status = "Completed", HasNotes };
    object? IAuditEvent.Metadata        => null;
}

/// <summary>
/// PROB-07: distinct business event for a Pass outcome (see ProbationReviewCompletedAuditEvent's
/// remarks for why Pass/Fail/Extend are not folded into one generic "outcome recorded" event).
/// Carries enough structured, non-sensitive data (decision date, decision maker) to support a future
/// timeline UI reading directly from audit history — this audit trail is the timeline for this
/// event type; there is also a dedicated EmployeeTimelineEntry (ProbationPassed) written via
/// ProbationPassedIntegrationEvent for employee-facing display. Notes is free-text and excluded from
/// the payload — only a presence flag is recorded.
/// </summary>
internal sealed record ProbationPassedAuditEvent(
    Guid CompanyId,
    Guid ProbationRecordId,
    Guid ProbationReviewId,
    Guid EmployeeId,
    Guid DecisionMakerEmployeeId,
    DateOnly DecisionDate,
    bool HasNotes,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "probation-record.passed";
    string IAuditEvent.EntityType       => "ProbationRecord";
    Guid   IAuditEvent.EntityId         => ProbationRecordId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => DecisionMakerEmployeeId;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => "Probation passed";
    object? IAuditEvent.Before          => new { Status = "Active" };
    object? IAuditEvent.After           => new { Status = "Passed", DecisionDate, HasNotes };
    object? IAuditEvent.Metadata        => new { ProbationReviewId };
}

/// <summary>
/// PROB-07: distinct business event for a Fail outcome — see <see cref="ProbationPassedAuditEvent"/>
/// remarks. Also drives a dedicated EmployeeTimelineEntry (ProbationFailed) via
/// ProbationFailedIntegrationEvent, completing the "add timeline events for extended and failed
/// outcomes" requirement (previously only Pass had a timeline entry). Notes is free-text and
/// excluded from the payload.
/// </summary>
internal sealed record ProbationFailedAuditEvent(
    Guid CompanyId,
    Guid ProbationRecordId,
    Guid ProbationReviewId,
    Guid EmployeeId,
    Guid DecisionMakerEmployeeId,
    DateOnly DecisionDate,
    bool HasNotes,
    DateTimeOffset OccurredAt) : IAuditEvent
{
    string IAuditEvent.EventType        => "probation-record.failed";
    string IAuditEvent.EntityType       => "ProbationRecord";
    Guid   IAuditEvent.EntityId         => ProbationRecordId;
    Guid?  IAuditEvent.EmployeeId       => EmployeeId;
    Guid?  IAuditEvent.ActorUserId      => null;
    Guid?  IAuditEvent.ActorEmployeeId  => DecisionMakerEmployeeId;
    Guid?  IAuditEvent.CorrelationId    => null;
    string? IAuditEvent.Summary         => "Probation failed";
    object? IAuditEvent.Before          => new { Status = "Active" };
    object? IAuditEvent.After           => new { Status = "Failed", DecisionDate, HasNotes };
    object? IAuditEvent.Metadata        => new { ProbationReviewId };
}
