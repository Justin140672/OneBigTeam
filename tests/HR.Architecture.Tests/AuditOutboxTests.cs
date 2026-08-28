using HR.Infrastructure;
using HR.Infrastructure.Persistence;
using HR.SharedKernel;
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

        var publisher = new DbAuditEventPublisher(ctx, NullLogger<DbAuditEventPublisher>.Instance);

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
        var publisher = new DbAuditEventPublisher(ctx, NullLogger<DbAuditEventPublisher>.Instance);

        // A type that does NOT implement IAuditEvent — publisher should silently no-op.
        var exception = await Record.ExceptionAsync(
            () => publisher.PublishAsync("not an audit event", CancellationToken.None));

        Assert.Null(exception);
    }
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
