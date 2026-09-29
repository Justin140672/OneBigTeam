using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

public class AuditAppendOnlyTests
{
    private const string DummyConnectionString = "Host=localhost;Database=audit_append_only_unit_test";

    private static AuditDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(DummyConnectionString)
            .Options;
        return new AuditDbContext(options);
    }

    [Fact]
    public async Task SaveChangesAsync_Throws_When_Entry_Is_Modified()
    {
        await using var ctx = BuildContext();
        var entry = ctx.AuditEvents.Add(BuildMinimalAuditEvent());

        entry.State = EntityState.Modified;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_Throws_When_Entry_Is_Deleted()
    {
        await using var ctx = BuildContext();
        var entry = ctx.AuditEvents.Add(BuildMinimalAuditEvent());

        entry.State = EntityState.Deleted;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ctx.SaveChangesAsync());
    }

    [Fact]
    public void SaveChanges_Throws_When_Entry_Is_Modified()
    {
        using var ctx = BuildContext();
        var entry = ctx.AuditEvents.Add(BuildMinimalAuditEvent());

        entry.State = EntityState.Modified;

        Assert.Throws<InvalidOperationException>(() => ctx.SaveChanges());
    }

    [Fact]
    public void SaveChanges_Throws_When_Entry_Is_Deleted()
    {
        using var ctx = BuildContext();
        var entry = ctx.AuditEvents.Add(BuildMinimalAuditEvent());

        entry.State = EntityState.Deleted;

        Assert.Throws<InvalidOperationException>(() => ctx.SaveChanges());
    }

    [Fact]
    public void SaveChanges_Does_Not_Throw_InvalidOperationException_For_Added_Entry()
    {
        // EnforceAppendOnly must not block inserts — only Modified and Deleted are illegal.
        // Npgsql will fail with a connection error *after* the guard passes; we confirm the
        // guard itself is not the source of that failure.
        using var ctx = BuildContext();
        ctx.AuditEvents.Add(BuildMinimalAuditEvent());

        var ex = Record.Exception(() => ctx.SaveChanges());

        // EnforceAppendOnly must not be the source of any failure here.
        // Any exception from the DB layer (connection, SQL errors) is acceptable —
        // only an InvalidOperationException from the AUD-02 guard is prohibited.
        if (ex is InvalidOperationException ioe)
        {
            Assert.DoesNotContain("AUD-02", ioe.Message,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_Does_Not_Throw_AUD02_When_AuditPendingItem_Is_Modified()
    {
        // AuditPendingItem is a mutable staging table by design — the promotion job legitimately
        // transitions its Status (MarkProcessing → MarkCommitted/MarkFailed). The append-only guard
        // is scoped to committed AuditEvent rows and must not block this.
        await using var ctx = BuildContext();
        var entry = ctx.AuditPendingItems.Add(AuditPendingItem.From(new FakeAuditEvent()));

        entry.State = EntityState.Modified;

        var ex = await Record.ExceptionAsync(() => ctx.SaveChangesAsync());

        if (ex is InvalidOperationException ioe)
            Assert.DoesNotContain("AUD-02", ioe.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveChanges_Does_Not_Throw_AUD02_When_AuditPendingItem_Is_Deleted()
    {
        using var ctx = BuildContext();
        var entry = ctx.AuditPendingItems.Add(AuditPendingItem.From(new FakeAuditEvent()));

        entry.State = EntityState.Deleted;

        var ex = Record.Exception(() => ctx.SaveChanges());

        if (ex is InvalidOperationException ioe)
            Assert.DoesNotContain("AUD-02", ioe.Message, StringComparison.Ordinal);
    }

    private static AuditEvent BuildMinimalAuditEvent() =>
        AuditEvent.From(new FakeAuditEvent());
}

internal sealed class FakeAuditEvent : HR.SharedKernel.IAuditEvent
{
    public Guid           CompanyId       => Guid.NewGuid();
    public string         EventType       => "test.event";
    public string         EntityType      => "Test";
    public Guid           EntityId        => Guid.NewGuid();
    public Guid?          ActorUserId     => Guid.Parse("00000000-0000-0000-0000-000000000001");
    public Guid?          ActorEmployeeId => null;
    public DateTimeOffset OccurredAt      => DateTimeOffset.UtcNow;
    public Guid?          CorrelationId   => null;
    public string?        Summary         => null;
    public object?        Before          => null;
    public object?        After           => null;
    public object?        Metadata        => null;
}
