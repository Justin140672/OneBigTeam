using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests;

/// <summary>
/// Ticket 23 (P2): durable correlation/causation/message-id metadata on
/// <see cref="InviteAcceptanceOperation"/> — extends the pattern already established for
/// HR.Modules.Employees (the reference module; see
/// HR.Modules.Employees.Tests.AuditOutboxMetadataTests) to Identity's invite-acceptance durable
/// operation and its owning reconciliation job (<see cref="InviteAcceptanceReconciliationJob"/>).
/// Reference-wiring coverage for the third call site (Features/AcceptInvite/Endpoint.cs) is not
/// duplicated here — the endpoint's own optional-parameter wiring is identical in shape to
/// CompleteTaskHandler/PurgeEligibleCandidatesHandler's, and AcceptInvite's FastEndpoints
/// construction makes it awkward to unit-invoke directly; the CreatePending stamping behaviour
/// itself (asserted below) is what the endpoint actually relies on.
/// </summary>
[Collection("IdentityDatabase")]
public class Ticket23InviteAcceptanceOperationMetadataTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 21, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    // ── CreatePending: stamping from a supplied IExecutionContext ───────────────────────────────

    [Fact]
    public void CreatePending_With_Supplied_Context_Stamps_CorrelationId_From_Context_CorrelationId_And_CausationId_From_Context_MessageId()
    {
        var context = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        var operation = InviteAcceptanceOperation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invitee@example.com", CreatedAt, context);

        Assert.Equal(context.MessageId, operation.CorrelationId);
        Assert.Equal(context.MessageId, operation.CausationId);
        Assert.NotNull(operation.MessageId);
        Assert.NotEqual(Guid.Empty, operation.MessageId!.Value);
        Assert.NotEqual(context.MessageId, operation.MessageId!.Value);
    }

    [Fact]
    public void CreatePending_With_No_Context_Leaves_Correlation_And_Causation_Null_But_Still_Mints_A_Fresh_MessageId()
    {
        var operation = InviteAcceptanceOperation.CreatePending(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "invitee@example.com", CreatedAt);

        Assert.Null(operation.CorrelationId);
        Assert.Null(operation.CausationId);
        Assert.NotNull(operation.MessageId);
        Assert.NotEqual(Guid.Empty, operation.MessageId!.Value);
    }

    // ── InviteAcceptanceReconciliationJob: restores persisted metadata as the ambient context ──

    // Captures whatever execution context is ambient (via the supplied accessor) at the moment the
    // reconciliation outcome's audit event is about to be published.
    private sealed class ContextCapturingAuditEventPublisher(IExecutionContextAccessor accessor) : IAuditEventPublisher
    {
        public IExecutionContext? ObservedDuringPublish { get; private set; }

        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            ObservedDuringPublish = accessor.Current;
            return Task.CompletedTask;
        }
    }

    private async Task<(InviteAcceptanceOperation Operation, UserInvite Invite)> SeedStaleOperationAsync(
        Guid? invitationCompanyId, Guid? employeeId, IExecutionContext? executionContext)
    {
        var companyId = invitationCompanyId ?? Guid.NewGuid();
        var empId = employeeId ?? Guid.NewGuid();
        var email = $"ticket23.{Guid.NewGuid():N}@example.com";

        var invite = UserInvite.Create(empId, companyId, email, CreatedAt);
        invite.Cancel(CreatedAt.AddMinutes(2));

        var operation = InviteAcceptanceOperation.CreatePending(
            Guid.NewGuid(), invite.Id, companyId, empId, email, CreatedAt, executionContext);

        await using var db = fixture.BuildContext();
        db.UserInvites.Add(invite);
        db.InviteAcceptanceOperations.Add(operation);
        await db.SaveChangesAsync();

        // Force CreatedAt stale enough for the reconciliation sweep to pick it up as a stale
        // Pending operation.
        db.Entry(operation).Property("CreatedAt").CurrentValue = Now.AddMinutes(-20);
        await db.SaveChangesAsync();

        return (operation, invite);
    }

    [Fact]
    public async Task ExecuteAsync_Restores_Persisted_CorrelationId_CausationId_And_MessageId_As_The_Ambient_Context()
    {
        var executionContext = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        var (operation, _) = await SeedStaleOperationAsync(null, null, executionContext);

        var accessor = new ExecutionContextAccessor();
        var auditPublisher = new ContextCapturingAuditEventPublisher(accessor);
        await using var db = fixture.BuildContext();
        var job = new InviteAcceptanceReconciliationJob(
            db, new FakeSupabaseAuthGateway(), Clock, auditPublisher,
            NullLogger<InviteAcceptanceReconciliationJob>.Instance, accessor);

        await job.ExecuteAsync();

        Assert.NotNull(auditPublisher.ObservedDuringPublish);
        var restored = auditPublisher.ObservedDuringPublish!;
        Assert.Equal(executionContext.CorrelationId, restored.CorrelationId);
        Assert.Equal(executionContext.MessageId, restored.CausationId);
        Assert.Equal(ExecutionOrigin.ReconciliationJob, restored.Origin);

        var reloaded = await db.InviteAcceptanceOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(InviteAcceptanceOperation.StatusCancelled, reloaded.Status);
    }

    [Fact]
    public async Task ExecuteAsync_Legacy_Row_With_Null_Metadata_Still_Restores_A_Fresh_Root_Context_Rather_Than_Throwing()
    {
        // No supplied context — simulates a row written before this migration.
        var (operation, _) = await SeedStaleOperationAsync(null, null, executionContext: null);

        var accessor = new ExecutionContextAccessor();
        var auditPublisher = new ContextCapturingAuditEventPublisher(accessor);
        await using var db = fixture.BuildContext();
        var job = new InviteAcceptanceReconciliationJob(
            db, new FakeSupabaseAuthGateway(), Clock, auditPublisher,
            NullLogger<InviteAcceptanceReconciliationJob>.Instance, accessor);

        var exception = await Record.ExceptionAsync(() => job.ExecuteAsync());

        Assert.Null(exception);
        Assert.NotNull(auditPublisher.ObservedDuringPublish);
        Assert.Equal(ExecutionOrigin.ReconciliationJob, auditPublisher.ObservedDuringPublish!.Origin);
        Assert.Null(auditPublisher.ObservedDuringPublish.CausationId);

        var reloaded = await db.InviteAcceptanceOperations.SingleAsync(o => o.Id == operation.Id);
        Assert.Equal(InviteAcceptanceOperation.StatusCancelled, reloaded.Status);
    }
}
