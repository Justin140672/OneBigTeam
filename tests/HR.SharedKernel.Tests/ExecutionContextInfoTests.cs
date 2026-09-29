using HR.SharedKernel.ExecutionContext;

namespace HR.SharedKernel.Tests;

/// <summary>
/// Ticket 23 (P2): pins the exact correlation/causation/message id relationships produced by each
/// of <see cref="ExecutionContextInfo"/>'s static factory methods.
/// </summary>
public class ExecutionContextInfoTests
{

    [Fact]
    public void NewRoot_Sets_CorrelationId_Equal_To_MessageId()
    {
        var ctx = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        Assert.Equal(ctx.MessageId.ToString("D"), ctx.CorrelationId);
        Assert.False(string.IsNullOrWhiteSpace(ctx.CorrelationId));
    }

    [Fact]
    public void NewRoot_Sets_CausationId_Null()
    {
        var ctx = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        Assert.Null(ctx.CausationId);
    }

    [Fact]
    public void NewRoot_Two_Calls_Produce_Different_Ids()
    {
        var a = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        var b = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        Assert.NotEqual(a.CorrelationId, b.CorrelationId);
        Assert.NotEqual(a.MessageId, b.MessageId);
    }

    [Fact]
    public void NewRoot_Carries_Origin_And_Optional_Fields()
    {
        var actorUserId = Guid.NewGuid();
        var actorEmployeeId = Guid.NewGuid();

        var ctx = ExecutionContextInfo.NewRoot(
            ExecutionOrigin.ScheduledJob,
            traceId: "trace-123",
            actorUserId: actorUserId,
            actorEmployeeId: actorEmployeeId,
            actorType: AuditActorType.ScheduledJob);

        Assert.Equal(ExecutionOrigin.ScheduledJob, ctx.Origin);
        Assert.Equal("trace-123", ctx.TraceId);
        Assert.Equal(actorUserId, ctx.ActorUserId);
        Assert.Equal(actorEmployeeId, ctx.ActorEmployeeId);
        Assert.Equal(AuditActorType.ScheduledJob, ctx.ActorType);
    }

    [Fact]
    public void NewRoot_Defaults_ActorType_To_Human_When_Not_Specified()
    {
        var ctx = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        Assert.Equal(AuditActorType.Human, ctx.ActorType);
    }


    [Fact]
    public void CausedBy_Preserves_Parent_CorrelationId()
    {
        var parent = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        var child = ExecutionContextInfo.CausedBy(parent, ExecutionOrigin.IntegrationEvent);

        Assert.Equal(parent.CorrelationId, child.CorrelationId);
    }

    [Fact]
    public void CausedBy_Sets_CausationId_To_Parent_MessageId()
    {
        var parent = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        var child = ExecutionContextInfo.CausedBy(parent, ExecutionOrigin.IntegrationEvent);

        Assert.Equal(parent.MessageId, child.CausationId);
    }

    [Fact]
    public void CausedBy_Mints_A_New_MessageId_Distinct_From_Parent()
    {
        var parent = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        var child = ExecutionContextInfo.CausedBy(parent, ExecutionOrigin.IntegrationEvent);

        Assert.NotEqual(parent.MessageId, child.MessageId);
    }

    [Fact]
    public void CausedBy_Inherits_Actor_And_TraceId_When_Not_Overridden()
    {
        var actorUserId = Guid.NewGuid();
        var actorEmployeeId = Guid.NewGuid();
        var parent = ExecutionContextInfo.NewRoot(
            ExecutionOrigin.HttpRequest, traceId: "parent-trace",
            actorUserId: actorUserId, actorEmployeeId: actorEmployeeId, actorType: AuditActorType.ScheduledJob);

        var child = ExecutionContextInfo.CausedBy(parent, ExecutionOrigin.IntegrationEvent);

        Assert.Equal("parent-trace", child.TraceId);
        Assert.Equal(actorUserId, child.ActorUserId);
        Assert.Equal(actorEmployeeId, child.ActorEmployeeId);
        Assert.Equal(AuditActorType.ScheduledJob, child.ActorType);
    }

    [Fact]
    public void CausedBy_Overrides_TraceId_When_Explicitly_Supplied()
    {
        var parent = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest, traceId: "parent-trace");

        var child = ExecutionContextInfo.CausedBy(parent, ExecutionOrigin.IntegrationEvent, traceId: "child-trace");

        Assert.Equal("child-trace", child.TraceId);
    }

    [Fact]
    public void CausedBy_Sets_Requested_Origin()
    {
        var parent = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);

        var child = ExecutionContextInfo.CausedBy(parent, ExecutionOrigin.ManualRetry);

        Assert.Equal(ExecutionOrigin.ManualRetry, child.Origin);
    }

    [Fact]
    public void CausedBy_Throws_When_Parent_Is_Null()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ExecutionContextInfo.CausedBy(null!, ExecutionOrigin.IntegrationEvent));
    }

    [Fact]
    public void CausedBy_Chain_Of_Two_Preserves_Root_CorrelationId_Through_Both_Hops()
    {
        var a = ExecutionContextInfo.NewRoot(ExecutionOrigin.HttpRequest);
        var b = ExecutionContextInfo.CausedBy(a, ExecutionOrigin.IntegrationEvent);
        var c = ExecutionContextInfo.CausedBy(b, ExecutionOrigin.IntegrationEvent);

        Assert.Equal(a.CorrelationId, c.CorrelationId);
        Assert.Equal(b.MessageId, c.CausationId);
        Assert.NotEqual(a.MessageId, c.CausationId);
    }


    [Fact]
    public void Restore_Reconstructs_Exact_Persisted_Identity()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var messageId = Guid.NewGuid();
        var causationId = Guid.NewGuid();

        var ctx = ExecutionContextInfo.Restore(correlationId, messageId, causationId, ExecutionOrigin.ReconciliationJob);

        Assert.Equal(correlationId, ctx.CorrelationId);
        Assert.Equal(messageId, ctx.MessageId);
        Assert.Equal(causationId, ctx.CausationId);
        Assert.Equal(ExecutionOrigin.ReconciliationJob, ctx.Origin);
    }

    [Fact]
    public void Restore_Allows_Null_CausationId_For_A_Restored_Root_Message()
    {
        var ctx = ExecutionContextInfo.Restore(Guid.NewGuid().ToString("D"), Guid.NewGuid(), null, ExecutionOrigin.ReconciliationJob);

        Assert.Null(ctx.CausationId);
    }

    [Fact]
    public void Restore_Defaults_ActorType_To_IntegrationHandler_When_Not_Specified()
    {
        var ctx = ExecutionContextInfo.Restore(Guid.NewGuid().ToString("D"), Guid.NewGuid(), null, ExecutionOrigin.ReconciliationJob);

        Assert.Equal(AuditActorType.IntegrationHandler, ctx.ActorType);
    }

    [Fact]
    public void Restore_Carries_Optional_Actor_And_TraceId_When_Supplied()
    {
        var actorUserId = Guid.NewGuid();
        var actorEmployeeId = Guid.NewGuid();

        var ctx = ExecutionContextInfo.Restore(
            Guid.NewGuid().ToString("D"), Guid.NewGuid(), null, ExecutionOrigin.ManualRetry,
            actorUserId: actorUserId, actorEmployeeId: actorEmployeeId,
            actorType: AuditActorType.Human, traceId: "restored-trace");

        Assert.Equal(actorUserId, ctx.ActorUserId);
        Assert.Equal(actorEmployeeId, ctx.ActorEmployeeId);
        Assert.Equal(AuditActorType.Human, ctx.ActorType);
        Assert.Equal("restored-trace", ctx.TraceId);
    }


    [Fact]
    public void Recovered_Preserves_CorrelationId()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var recoveredMessageId = Guid.NewGuid();

        var ctx = ExecutionContextInfo.Recovered(correlationId, recoveredMessageId);

        Assert.Equal(correlationId, ctx.CorrelationId);
    }

    [Fact]
    public void Recovered_Sets_CausationId_To_Recovered_Operation_MessageId()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var recoveredMessageId = Guid.NewGuid();

        var ctx = ExecutionContextInfo.Recovered(correlationId, recoveredMessageId);

        Assert.Equal(recoveredMessageId, ctx.CausationId);
    }

    [Fact]
    public void Recovered_Mints_A_New_MessageId_Distinct_From_Recovered_Operation()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var recoveredMessageId = Guid.NewGuid();

        var ctx = ExecutionContextInfo.Recovered(correlationId, recoveredMessageId);

        Assert.NotEqual(recoveredMessageId, ctx.MessageId);
    }

    [Fact]
    public void Recovered_Sets_Origin_To_ReconciliationJob()
    {
        var ctx = ExecutionContextInfo.Recovered(Guid.NewGuid().ToString("D"), Guid.NewGuid());

        Assert.Equal(ExecutionOrigin.ReconciliationJob, ctx.Origin);
    }

    // ── CorrelationIdGuid (ticket 23 follow-up: string correlation -> Guid persistence mapping) ──

    [Fact]
    public void CorrelationIdGuid_Derive_Returns_Same_Guid_For_A_Guid_Shaped_String()
    {
        var guid = Guid.NewGuid();

        var derived = CorrelationIdGuid.Derive(guid.ToString("D"));

        Assert.Equal(guid, derived);
    }

    [Fact]
    public void CorrelationIdGuid_Derive_Is_Deterministic_For_An_Opaque_String()
    {
        var opaque = "e2e-" + Guid.NewGuid().ToString("N");

        var first = CorrelationIdGuid.Derive(opaque);
        var second = CorrelationIdGuid.Derive(opaque);

        Assert.Equal(first, second);
    }

    [Fact]
    public void CorrelationIdGuid_Derive_Produces_Different_Guids_For_Different_Opaque_Strings()
    {
        var a = CorrelationIdGuid.Derive("e2e-" + Guid.NewGuid().ToString("N"));
        var b = CorrelationIdGuid.Derive("e2e-" + Guid.NewGuid().ToString("N"));

        Assert.NotEqual(a, b);
    }
}
