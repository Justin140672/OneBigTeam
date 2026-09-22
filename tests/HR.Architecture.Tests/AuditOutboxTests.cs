using System.Text.Json;
using HR.Infrastructure;
using HR.Infrastructure.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Architecture.Tests;

/// <summary>
/// AUD-01: unit tests for the audit outbox state machine and publisher behaviour.
/// No database required — AuditPendingItem state transitions are pure C#, and
/// DbAuditEventPublisher failure-handling is tested via a Npgsql context with a dummy
/// connection string (save attempt fails at the transport layer, not the guard layer).
/// </summary>
public class AuditOutboxTests
{
    private const string DummyConnectionString = "Host=localhost;Database=audit_outbox_unit_test";

    // ── AuditPendingItem state machine ────────────────────────────────────────────

    [Fact]
    public void From_Captures_EventId_And_Status_Pending()
    {
        var evt = new StableAuditEvent();
        var item = AuditPendingItem.From(evt);

        Assert.Equal(evt.EventId, item.EventId);
        Assert.Equal(AuditPendingItem.StatusPending, item.Status);
        Assert.Equal(0, item.AttemptCount);
    }

    [Fact]
    public void MarkProcessing_Increments_AttemptCount_And_Clears_Error()
    {
        var item = AuditPendingItem.From(new StableAuditEvent());
        item.MarkProcessing();
        item.MarkFailed("something went wrong");

        item.MarkProcessing(); // second attempt
        Assert.Equal(AuditPendingItem.StatusProcessing, item.Status);
        Assert.Equal(2, item.AttemptCount);
        Assert.Null(item.ErrorMessage);
    }

    [Fact]
    public void MarkCommitted_Sets_Status_And_ProcessedAt()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var item = AuditPendingItem.From(new StableAuditEvent());
        item.MarkProcessing();
        item.MarkCommitted(now);

        Assert.Equal(AuditPendingItem.StatusCommitted, item.Status);
        Assert.Equal(now, item.ProcessedAt);
    }

    [Fact]
    public void MarkFailed_Sets_Status_And_ErrorMessage()
    {
        var item = AuditPendingItem.From(new StableAuditEvent());
        item.MarkProcessing();
        item.MarkFailed("network error");

        Assert.Equal(AuditPendingItem.StatusFailed, item.Status);
        Assert.Equal("network error", item.ErrorMessage);
    }

    [Fact]
    public void MarkFailed_Truncates_ErrorMessage_To_2000_Characters()
    {
        var item = AuditPendingItem.From(new StableAuditEvent());
        item.MarkFailed(new string('x', 3000));

        Assert.Equal(2000, item.ErrorMessage!.Length);
    }

    [Fact]
    public void ResetForRetry_Returns_To_Pending_From_Failed()
    {
        var item = AuditPendingItem.From(new StableAuditEvent());
        item.MarkProcessing();
        item.MarkFailed("transient error");

        item.ResetForRetry();

        Assert.Equal(AuditPendingItem.StatusPending, item.Status);
        Assert.Null(item.ErrorMessage);
    }

    [Fact]
    public void ResetForRetry_Throws_When_Not_In_Failed_State()
    {
        var item = AuditPendingItem.From(new StableAuditEvent());
        item.MarkProcessing();
        item.MarkCommitted(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => item.ResetForRetry());
    }

    // ── DbAuditEventPublisher ────────────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_Does_Not_Throw_When_Save_Fails()
    {
        // Arrange — context with a dummy connection string; SaveChangesAsync will throw at the
        // transport layer (cannot connect), not at the EnforceAppendOnly layer.
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(DummyConnectionString)
            .Options;
        await using var ctx = new AuditDbContext(options);

        var publisher = new DbAuditEventPublisher(ctx, NullLogger<DbAuditEventPublisher>.Instance, new HR.SharedKernel.ExecutionContext.ExecutionContextAccessor());

        // Act — must not throw; the publisher logs the failure instead.
        var exception = await Record.ExceptionAsync(
            () => publisher.PublishAsync(new StableAuditEvent(), CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task PublishAsync_Ignores_Non_IAuditEvent_Types()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(DummyConnectionString)
            .Options;
        await using var ctx = new AuditDbContext(options);
        var publisher = new DbAuditEventPublisher(ctx, NullLogger<DbAuditEventPublisher>.Instance, new HR.SharedKernel.ExecutionContext.ExecutionContextAccessor());

        // A type that does NOT implement IAuditEvent — publisher should silently no-op.
        var exception = await Record.ExceptionAsync(
            () => publisher.PublishAsync("not an audit event", CancellationToken.None));

        Assert.Null(exception);
    }

    // ── Ticket 23 (P2): central audit correlation default ──────────────────────────
    // Ticket 23 (P2) follow-up: a central "default the ambient execution context's correlation id
    // onto every audit event published via IAuditEventPublisher.PublishAsync" was implemented here,
    // then REVERTED after it broke HR.Modules.Employees.Features.GetEmployeeAuditHistory.Handler
    // .MergeCorrelatedItems — that pre-existing feature already repurposes IAuditEvent.CorrelationId
    // as a narrow, EXPLICIT merge key shared deliberately between exactly two coordinated requests
    // (the combined Employee+Employment tab save). Defaulting every audit event raised within the
    // same HTTP request to the SAME ambient correlation id made every unrelated audit row from that
    // request look like part of that merge group, silently collapsing them into one displayed item
    // (reproduced via LeavingProcessLifecycleEndToEndTests.
    // Full_Leaving_Process_Lifecycle_Starts_Offboarding_Records_Audit_And_Amends). See
    // DbAuditEventPublisher.PublishAsync's remarks for what still carries per-request technical
    // correlation instead (request logs, integration-event causation chaining, the durable
    // outbox/operation tables' dedicated correlation_id columns). An audit event that explicitly
    // sets its own CorrelationId (e.g. ExplicitCorrelationAuditEvent below) continues to have that
    // value persisted unchanged — DbAuditEventPublisher never touches CorrelationId at all now.
}

/// <summary>
/// Audit event with a stable, fixed EventId suitable for idempotency tests.
/// </summary>
internal sealed class StableAuditEvent : IAuditEvent
{
    public Guid EventId        { get; } = Guid.NewGuid(); // fixed per instance
    public Guid CompanyId      { get; } = Guid.NewGuid();
    public string EventType    => "test.aud01";
    public string EntityType   => "TestEntity";
    public Guid EntityId       { get; } = Guid.NewGuid();
    // AUD-04: test fixture uses a fixed actor so the attribution guard passes.
    public Guid? ActorUserId   => Guid.Parse("00000000-0000-0000-0000-000000000001");
    public Guid? ActorEmployeeId => null;
    public DateTimeOffset OccurredAt => DateTimeOffset.UtcNow;
    public Guid? CorrelationId => null;
    public string? Summary     => "AUD-01 unit test event";
    public object? Before      => null;
    public object? After       => null;
    public object? Metadata    => null;
}

/// <summary>Audit event fixture with an explicit, non-null CorrelationId already set — used to
/// document that <see cref="DbAuditEventPublisher"/> must never override it.</summary>
internal sealed class ExplicitCorrelationAuditEvent(Guid correlationId) : IAuditEvent
{
    public Guid EventId        { get; } = Guid.NewGuid();
    public Guid CompanyId      { get; } = Guid.NewGuid();
    public string EventType    => "test.aud01.explicit-correlation";
    public string EntityType   => "TestEntity";
    public Guid EntityId       { get; } = Guid.NewGuid();
    public Guid? ActorUserId   => Guid.Parse("00000000-0000-0000-0000-000000000001");
    public Guid? ActorEmployeeId => null;
    public DateTimeOffset OccurredAt => DateTimeOffset.UtcNow;
    public Guid? CorrelationId => correlationId;
    public string? Summary     => "AUD-01 unit test event with explicit correlation id";
    public object? Before      => null;
    public object? After       => null;
    public object? Metadata    => null;
}
