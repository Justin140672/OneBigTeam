using System.Globalization;
using HR.Infrastructure.Abstractions;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 4 final follow-up (Finding 2), PostgreSQL-backed: the audit log is the one genuinely
/// unbounded export source, so <see cref="IAuditDataExportSource"/> streams it via a stable
/// <c>(OccurredAt, Id)</c> keyset cursor rather than a single <c>ToListAsync()</c>.
///
/// The earlier version of this fixture built its data by <em>insertion index</em> and merely hoped a
/// block of rows landed on the page boundary. This version constructs the data <b>by final sort
/// order</b> and inserts it shuffled, so the assertions pin the real Postgres behaviour: a group of
/// rows sharing one exact timestamp <c>T</c> occupies final positions 991..1020 — straddling the
/// first 1,000-row page boundary — and every one of them must be emitted exactly once, in descending
/// <c>(OccurredAt, Id)</c> order, with the <c>Id</c> tie-break resolved by Postgres' own uuid
/// ordering (not .NET's <see cref="Guid"/> comparison).
///
/// The EF in-memory coverage in <c>AuditDataExportSourceTests</c> stays as-is; this is the real-SQL pin.
/// </summary>
[Collection("Integration")]
public class OrganisationDataExportAuditPaginationTests
{
    private const int PageSize = 1_000;
    private const int SummaryColumn = 7;

    private readonly ApiWebApplicationFactory _factory;

    public OrganisationDataExportAuditPaginationTests(ApiWebApplicationFactory factory) => _factory = factory;

    private sealed record SeedAuditEvent : IAuditEvent
    {
        public Guid EventId { get; init; } = Guid.NewGuid();
        public Guid CompanyId { get; init; }
        AuditActorType IAuditEvent.ActorType => AuditActorType.ScheduledJob;
        public string EventType { get; init; } = "test.export.pagination";
        public string EntityType { get; init; } = "PaginationProbe";
        public Guid EntityId { get; init; } = Guid.NewGuid();
        public Guid? EmployeeId => null;
        public Guid? ActorUserId => null;
        public Guid? ActorEmployeeId => null;
        public DateTimeOffset OccurredAt { get; init; }
        public Guid? CorrelationId => null;
        public string? Summary { get; init; }
        public object? Before => null;
        public object? After => null;
        public object? Metadata => null;
    }

    [Fact]
    public async Task Streams_every_target_row_once_including_a_30_row_timestamp_tie_that_crosses_the_page_boundary()
    {
        var target = Guid.NewGuid();
        var other = Guid.NewGuid();

        var t = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        var targetSeed = new List<SeedAuditEvent>(2_500);
        var tiedMarkers = new List<string>(30);
        for (var pos = 1; pos <= 2_500; pos++)
        {
            DateTimeOffset occurredAt;
            string marker;
            if (pos <= 990)
            {
                occurredAt = t.AddSeconds(991 - pos);
                marker = $"tgt#{pos:0000}";
            }
            else if (pos <= 1_020)
            {
                occurredAt = t;
                marker = $"tie#{pos - 991:00}";
                tiedMarkers.Add(marker);
            }
            else
            {
                occurredAt = t.AddSeconds(-(pos - 1_020));
                marker = $"tgt#{pos:0000}";
            }

            targetSeed.Add(new SeedAuditEvent { CompanyId = target, OccurredAt = occurredAt, Summary = marker });
        }

        var targetMarkers = targetSeed.Select(e => e.Summary!).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(2_500, targetMarkers.Count);
        Assert.Equal(30, tiedMarkers.Count);

        var otherSeed = Enumerable.Range(0, 60)
            .Select(k => new SeedAuditEvent
            {
                CompanyId = other,
                OccurredAt = t.AddSeconds(30 - k),
                Summary = $"oth#{k:00}",
            })
            .ToList();

        var toInsert = targetSeed.Concat(otherSeed).ToList();
        var rng = new Random(20_260_910);
        for (var i = toInsert.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (toInsert[i], toInsert[j]) = (toInsert[j], toInsert[i]);
        }

        try
        {
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
                foreach (var e in toInsert)
                    db.AuditEvents.Add(AuditEvent.From(e));
                await db.SaveChangesAsync();
            }

            List<IReadOnlyList<string?>> rows;
            using (var scope = _factory.Services.CreateScope())
            {
                var source = scope.ServiceProvider.GetRequiredService<IAuditDataExportSource>();
                var tables = await source.GetTablesAsync(target, CancellationToken.None);
                var table = Assert.Single(tables);
                Assert.NotNull(table.RowStream);

                rows = [];
                await foreach (var row in table.RowStream!(CancellationToken.None))
                    rows.Add(row);
            }

            var exportedMarkers = rows.Select(r => r[SummaryColumn]).ToList();

            Assert.Equal(2_500, exportedMarkers.Count);
            Assert.Equal(2_500, exportedMarkers.Distinct().Count());
            Assert.Equal(
                targetMarkers.OrderBy(x => x, StringComparer.Ordinal),
                exportedMarkers.Select(m => m!).OrderBy(x => x, StringComparer.Ordinal));

            var exportedTie = exportedMarkers.Where(m => m is not null && m.StartsWith("tie#", StringComparison.Ordinal))
                .Select(m => m!)
                .ToList();
            Assert.Equal(30, exportedTie.Count);
            Assert.Equal(tiedMarkers.OrderBy(x => x, StringComparer.Ordinal), exportedTie.OrderBy(x => x, StringComparer.Ordinal));

            Assert.DoesNotContain(exportedMarkers, m => m is not null && m.StartsWith("oth#", StringComparison.Ordinal));

            var occurredAt = rows
                .Select(r => DateTimeOffset.Parse(r[0]!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind))
                .ToList();
            Assert.Equal(occurredAt.OrderByDescending(x => x).ToList(), occurredAt);

            List<string> dbTieOrderById;
            Dictionary<string, Guid> tieMarkerToId;
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
                var tied = await db.AuditEvents
                    .AsNoTracking()
                    .Where(e => e.CompanyId == target && e.OccurredAt == t)
                    .OrderByDescending(e => e.Id)
                    .Select(e => new { e.Summary, e.Id })
                    .ToListAsync();
                dbTieOrderById = tied.Select(x => x.Summary!).ToList();
                tieMarkerToId = tied.ToDictionary(x => x.Summary!, x => x.Id, StringComparer.Ordinal);
            }

            Assert.Equal(dbTieOrderById, exportedTie);

            var exportedTieIds = exportedTie.Select(m => tieMarkerToId[m]).ToList();
            Assert.Equal(30, exportedTieIds.Distinct().Count());

            var tieIndexes = exportedMarkers
                .Select((m, idx) => (m, idx))
                .Where(x => x.m is not null && x.m.StartsWith("tie#", StringComparison.Ordinal))
                .Select(x => x.idx)
                .ToList();
            Assert.True(tieIndexes.Min() < PageSize,
                $"first tied row was at export index {tieIndexes.Min()} — expected it to start on page 1 (< {PageSize})");
            Assert.True(tieIndexes.Max() >= PageSize,
                $"last tied row was at export index {tieIndexes.Max()} — expected it to spill onto page 2 (>= {PageSize})");
        }
        finally
        {
            try
            {
                using var scope = _factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
                await db.AuditEvents.Where(e => e.CompanyId == target || e.CompanyId == other).ExecuteDeleteAsync();
            }
            catch
            {
            }
        }
    }
}
