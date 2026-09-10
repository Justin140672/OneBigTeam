using System.Globalization;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Tests;

/// <summary>
/// Ticket 4 (memory hardening): <see cref="AuditDataExportSource"/> now streams the company's audit
/// rows via a stable <c>(OccurredAt, Id)</c> keyset cursor instead of a single <c>ToListAsync()</c>.
/// These tests pin that every row is emitted exactly once across page boundaries (including the hard
/// case of many rows sharing one <c>OccurredAt</c>), newest-first ordering is preserved, company
/// isolation holds, cancellation propagates mid-stream, and per-cell formatting is unchanged.
/// </summary>
public class AuditDataExportSourceTests
{
    private const int PageSize = 1_000;
    private static readonly DateTimeOffset Base = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static AuditDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AuditDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed record TestAuditEvent : IAuditEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public Guid CompanyId { get; init; }
        public string EventType { get; init; } = "Thing.Updated";
        public string EntityType { get; init; } = "Thing";
        public Guid EntityId { get; init; } = Guid.NewGuid();
        public Guid? EmployeeId { get; init; }
        public Guid? ActorUserId { get; init; }
        public Guid? ActorEmployeeId { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
        public Guid? CorrelationId { get; init; }
        public string? Summary { get; init; }
        public object? Before => null;
        public object? After => null;
        public object? Metadata => null;
    }

    private static async Task SeedAsync(AuditDbContext context, IEnumerable<IAuditEvent> events)
    {
        foreach (var e in events)
            context.AuditEvents.Add(AuditEvent.From(e));
        await context.SaveChangesAsync();
    }

    private static async Task<List<IReadOnlyList<string?>>> DrainAsync(
        AuditDataExportSource source, Guid companyId, CancellationToken cancellationToken = default)
    {
        var tables = await source.GetTablesAsync(companyId, cancellationToken);
        var table = Assert.Single(tables);
        Assert.Equal("audit_log", table.Name);
        Assert.NotNull(table.RowStream);

        var rows = new List<IReadOnlyList<string?>>();
        await foreach (var row in table.RowStream!(cancellationToken))
            rows.Add(row);
        return rows;
    }

    [Fact]
    public async Task Returns_a_single_streamed_audit_log_table()
    {
        await using var context = NewContext();
        var company = Guid.NewGuid();
        await SeedAsync(context, [new TestAuditEvent { CompanyId = company, OccurredAt = Base, Summary = "only" }]);

        var tables = await new AuditDataExportSource(context).GetTablesAsync(company, CancellationToken.None);

        var table = Assert.Single(tables);
        Assert.Equal("audit_log", table.Name);
        Assert.Equal(
            new[] { "OccurredAt", "EventType", "EntityType", "EntityId", "EmployeeId", "ActorUserId", "ActorEmployeeId", "Summary" },
            table.Columns.ToArray());
        Assert.NotNull(table.RowStream);
        Assert.Empty(table.Rows);
    }

    [Fact]
    public async Task Streams_every_row_exactly_once_across_multiple_keyset_pages()
    {
        await using var context = NewContext();
        var company = Guid.NewGuid();
        const int total = 2_500; // > 2 * PageSize
        var seeded = Enumerable.Range(0, total)
            .Select(i => new TestAuditEvent
            {
                CompanyId = company,
                OccurredAt = Base.AddSeconds(-i),
                Summary = $"evt-{i}",
            })
            .ToList();
        await SeedAsync(context, seeded);

        var rows = await DrainAsync(new AuditDataExportSource(context), company);

        Assert.Equal(total, rows.Count);
        var summaries = rows.Select(r => r[7]).ToList();
        Assert.Equal(total, summaries.Distinct().Count());
        Assert.Equal(seeded.Select(e => e.Summary).OrderBy(x => x), summaries.OrderBy(x => x));
    }

    [Fact]
    public async Task Rows_sharing_one_OccurredAt_are_never_skipped_or_duplicated_across_a_page_boundary()
    {
        await using var context = NewContext();
        var company = Guid.NewGuid();
        const int total = 1_010;

        // Rows 990..1009 all share a single timestamp that straddles the first page boundary (index 1000).
        var sharedInstant = Base.AddDays(-5);
        var seeded = Enumerable.Range(0, total)
            .Select(i => new TestAuditEvent
            {
                CompanyId = company,
                OccurredAt = (i is >= 990 and < 1010) ? sharedInstant : Base.AddSeconds(-i),
                Summary = $"evt-{i}",
            })
            .ToList();
        await SeedAsync(context, seeded);

        var rows = await DrainAsync(new AuditDataExportSource(context), company);

        Assert.Equal(total, rows.Count);
        Assert.Equal(
            seeded.Select(e => e.Summary).OrderBy(x => x),
            rows.Select(r => r[7]).OrderBy(x => x));
    }

    [Fact]
    public async Task Rows_are_ordered_newest_first()
    {
        await using var context = NewContext();
        var company = Guid.NewGuid();
        var seeded = Enumerable.Range(0, 2_100)
            .Select(i => new TestAuditEvent
            {
                CompanyId = company,
                OccurredAt = Base.AddMinutes(-i),
                Summary = $"evt-{i}",
            })
            .ToList();
        await SeedAsync(context, seeded);

        var rows = await DrainAsync(new AuditDataExportSource(context), company);

        var occurredAt = rows
            .Select(r => DateTimeOffset.Parse(r[0]!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
            .ToList();
        Assert.Equal(occurredAt.OrderByDescending(x => x), occurredAt);
    }

    [Fact]
    public async Task Rows_for_another_company_are_never_returned()
    {
        await using var context = NewContext();
        var company = Guid.NewGuid();
        var other = Guid.NewGuid();
        await SeedAsync(context,
        [
            new TestAuditEvent { CompanyId = company, OccurredAt = Base, Summary = "mine" },
            new TestAuditEvent { CompanyId = other, OccurredAt = Base.AddSeconds(1), Summary = "theirs" },
            new TestAuditEvent { CompanyId = other, OccurredAt = Base.AddSeconds(2), Summary = "theirs-2" },
        ]);

        var rows = await DrainAsync(new AuditDataExportSource(context), company);

        var row = Assert.Single(rows);
        Assert.Equal("mine", row[7]);
    }

    [Fact]
    public async Task Cancellation_after_the_first_page_stops_the_stream()
    {
        await using var context = NewContext();
        var company = Guid.NewGuid();
        await SeedAsync(context, Enumerable.Range(0, 2_500)
            .Select(i => new TestAuditEvent { CompanyId = company, OccurredAt = Base.AddSeconds(-i), Summary = $"evt-{i}" }));

        using var cts = new CancellationTokenSource();
        var source = new AuditDataExportSource(context);
        var tables = await source.GetTablesAsync(company, CancellationToken.None);
        var table = Assert.Single(tables);

        var seen = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in table.RowStream!(cts.Token))
            {
                seen++;
                if (seen == PageSize)
                    cts.Cancel();
            }
        });

        Assert.Equal(PageSize, seen); // stopped before fetching / emitting page 2
    }

    [Fact]
    public async Task EntityId_of_Guid_Empty_renders_as_an_empty_cell_and_OccurredAt_uses_round_trip_o_format()
    {
        await using var context = NewContext();
        var company = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await SeedAsync(context,
        [
            new TestAuditEvent
            {
                CompanyId = company,
                OccurredAt = Base,
                EntityId = Guid.Empty,
                EmployeeId = employeeId,
                Summary = "empty-entity",
            },
        ]);

        var rows = await DrainAsync(new AuditDataExportSource(context), company);

        var row = Assert.Single(rows);
        Assert.Null(row[3]); // EntityId == Guid.Empty -> null cell
        Assert.Equal(employeeId.ToString(), row[4]);
        Assert.Equal(Base.ToString("o", CultureInfo.InvariantCulture), row[0]);
    }

    [Fact]
    public async Task Non_empty_EntityId_renders_as_its_string_form()
    {
        await using var context = NewContext();
        var company = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        await SeedAsync(context,
            [new TestAuditEvent { CompanyId = company, OccurredAt = Base, EntityId = entityId, Summary = "x" }]);

        var rows = await DrainAsync(new AuditDataExportSource(context), company);

        Assert.Equal(entityId.ToString(), Assert.Single(rows)[3]);
    }

    [Fact]
    public async Task Empty_company_yields_no_rows()
    {
        await using var context = NewContext();

        var rows = await DrainAsync(new AuditDataExportSource(context), Guid.NewGuid());

        Assert.Empty(rows);
    }
}
