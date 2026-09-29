using HR.SharedKernel;

namespace HR.Modules.Probation;

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
    // Ticket 15 (P1): deterministic EventId derived from the extension-confirmation review id —
    // exactly one such review (and therefore one ProbationExtendedAuditEvent) is ever created per
    // extension decision (see ProbationExtensionService.ApplyAsync), so a recovered/replayed
    // dispatch can never create a duplicate audit row.
    Guid IAuditEvent.EventId            => ExtensionConfirmationReviewId;
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
    // Ticket 15 (P1): deterministic EventId derived from the review id — see ProbationPassedAuditEvent.
    Guid IAuditEvent.EventId            => ProbationReviewId;
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
    // Ticket 15 (P1): deterministic EventId derived from the review id — exactly one
    // ProbationPassedAuditEvent is ever meaningful per review, so a recovered/replayed dispatch
    // (see CompleteProbationReviewFromTaskAction) can never create a duplicate audit row.
    Guid IAuditEvent.EventId            => ProbationReviewId;
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
    // Ticket 15 (P1): deterministic EventId derived from the review id — see ProbationPassedAuditEvent.
    Guid IAuditEvent.EventId            => ProbationReviewId;
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
