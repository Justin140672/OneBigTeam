using HR.SharedKernel;

namespace HR.Modules.Tasks.Contracts;

/// <summary>
/// Implement this interface in any module to react when a task of a given
/// <see cref="TaskSource"/> is completed. Register as
/// <c>services.AddScoped&lt;ITaskCompletionAction, YourAction&gt;()</c>
/// in your module's DI setup; the Tasks module dispatcher will invoke all
/// registered implementations whose <see cref="Source"/> matches — BEFORE the underlying
/// TaskItem is actually marked Completed (see CompleteTaskHandler). A failed <see cref="Result"/>
/// aborts completion entirely: the task stays in its current (actionable) status and the caller
/// receives the failure. Implementations that have no required outcome/decision to validate
/// (e.g. a plain review/acknowledgement with no structured payload) should return
/// <see cref="Result.Success()"/> for the "nothing to validate" case — only return a failure when
/// the action genuinely could not be carried out.
///
/// Ticket 15 (P1) — REPLAY-SAFETY REQUIREMENT: <see cref="TaskCompletionReconciliationJob"/> (in
/// HR.Modules.Tasks) replays any <c>TaskCompletionOperation</c> left Pending by an interrupted
/// dispatch, calling <see cref="ExecuteAsync"/> again with the SAME
/// <see cref="TaskCompletionContext.DispatchOperationId"/>. Every implementation MUST therefore be
/// safe to call more than once for the same operation:
///  - Never treat "the primary domain entity is already in its resolved/terminal state" as proof
///    the WHOLE action (including its post-commit notifications, audits, follow-up task creation,
///    and integration events) already ran — only that its first durable write committed. Recover
///    any outstanding follow-up instead of unconditionally short-circuiting to
///    <see cref="Result.Success()"/>.
///  - Give every follow-up effect a deterministic, replay-stable identity so re-attempting it is a
///    safe no-op: an <c>ITaskCreator.CreateAsync</c> idempotency key derived from
///    <see cref="TaskCompletionContext.DispatchOperationId"/> or another stable entity id; a
///    notification keyed so <c>INotificationWriter</c>'s own (employee, source entity, type)
///    uniqueness (or an explicit <c>ExistsAsync</c> check) prevents a duplicate; and an
///    <c>IAuditEvent.EventId</c> override that is deterministic (not the default
///    <see cref="Guid.NewGuid()"/>) so <c>DbAuditEventPublisher</c>'s unique-index dedupe absorbs a
///    repeat publish.
///  - Only recover a follow-up when a stable dispatch identity is actually available
///    (<c>context.DispatchOperationId != Guid.Empty</c>) — an arbitrary already-resolved call with
///    no dispatch identity (e.g. a plain domain state that settled through some unrelated path)
///    must not have its effects unconditionally re-attempted.
/// See CompleteOnboardingTaskFromTaskAction, CompleteOffboardingTaskFromTaskAction,
/// CompleteProbationReviewFromTaskAction, SicknessEvidenceUploadCompletionAction,
/// InterviewFeedbackService.RecordFeedbackAsync, and AssetReturnService.ReturnAsync for the
/// reference implementations of this pattern, and
/// TaskCompletionActionReplaySafetyConformanceTests (HR.Architecture.Tests) for the enforced
/// registration inventory.
/// </summary>
public interface ITaskCompletionAction
{
    /// <summary>The task source this action handles.</summary>
    TaskSource Source { get; }

    /// <summary>The action type this implementation handles.</summary>
    TaskActionType ActionType { get; }

    Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken);
}
