using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Architecture.Tests;

/// <summary>
/// AUD-02: proves that AuditDbContext's application-layer append-only guard fires correctly.
///
/// EnforceAppendOnly() iterates ChangeTracker.Entries() and throws before EF dispatches any
/// SQL, so no database connection is required.  We configure Npgsql with a dummy connection
/// string so EF accepts the context at construction time; the guard always throws before the
/// provider attempts a connection.
///
/// Database-level enforcement (REVOKE UPDATE/DELETE on the runtime role) must be validated
/// separately via migration review and DBA sign-off.
/// </summary>
public class AuditAppendOnlyTests
{
    // Npgsql accepts this at configuration time; no connection is ever attempted because
    // EnforceAppendOnly() throws before the provider runs.
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

        // Force Modified without a DB round-trip — guard must fire regardless of how state
        // was reached.
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

        Assert.IsNotType<InvalidOperationException>(ex);
    }

    private static AuditEvent BuildMinimalAuditEvent() =>
        AuditEvent.From(new FakeAuditEvent());
}

/// <summary>
/// Minimal IAuditEvent stub for AUD-02 unit tests.
/// AuditEvent.From() is internal to HR.Infrastructure; the Architecture test project
/// calls it via the InternalsVisibleTo attribute on that assembly.
/// </summary>
internal sealed class FakeAuditEvent : HR.SharedKernel.IAuditEvent
{
    public Guid           CompanyId       => Guid.NewGuid();
    public string         EventType       => "test.event";
    public string         EntityType      => "Test";
    public Guid           EntityId        => Guid.NewGuid();
    public Guid?          ActorUserId     => null;
    public Guid?          ActorEmployeeId => null;
    public DateTimeOffset OccurredAt      => DateTimeOffset.UtcNow;
    public Guid?          CorrelationId   => null;
    public string?        Summary         => null;
    public object?        Before          => null;
    public object?        After           => null;
    public object?        Metadata        => null;
}
