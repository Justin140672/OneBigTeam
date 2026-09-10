using System.Globalization;
using System.Runtime.CompilerServices;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Persistence;

/// <summary>
/// Story 2: contributes the company's committed audit-log rows to the organisation data export.
/// Implemented here (not in a module) because audit persistence is a cross-cutting infrastructure
/// capability. Rows are already redacted by the audit scrubber before they reach the committed
/// table. company_id is enforced.
///
/// Ticket 4 (memory hardening): the audit log is the one genuinely unbounded export source
/// (~1.5M rows for a large tenant). Rows are streamed via keyset pagination — never a single
/// <c>ToListAsync()</c> of the whole set and never a second in-memory formatted-row collection.
/// The package builder writes each row straight to the CSV entry and discards it.
/// </summary>
internal sealed class AuditDataExportSource(AuditDbContext context) : IAuditDataExportSource
{
    /// <summary>
    /// Keyset page size. Audit rows are narrow (eight short string columns, a few hundred bytes
    /// formatted), so a page of 1,000 buffers well under 1 MiB while keeping the round-trip count for
    /// the ~1.5M-row worst case to ~1,500 — comfortably within the export's multi-minute budget.
    /// Sits in the middle of the 500–2,000 band called for by the ticket.
    /// </summary>
    private const int PageSize = 1_000;

    private static readonly string[] Columns =
        ["OccurredAt", "EventType", "EntityType", "EntityId", "EmployeeId", "ActorUserId", "ActorEmployeeId", "Summary"];

    public Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var table = DataExportTable.Streamed("audit_log", Columns, ct => StreamRowsAsync(companyId, ct));
        return Task.FromResult<IReadOnlyList<DataExportTable>>([table]);
    }

    /// <summary>
    /// Yields every committed audit row for the company, newest first (preserving the existing export
    /// order), paged by a stable <c>(OccurredAt, Id)</c> keyset cursor. The <c>Id</c> tie-breaker
    /// guarantees that events sharing an <c>OccurredAt</c> are never skipped or duplicated across a
    /// page boundary. Cancellation is honoured before each page fetch and while enumerating.
    /// </summary>
    private async IAsyncEnumerable<IReadOnlyList<string?>> StreamRowsAsync(
        Guid companyId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cursorOccurredAt = default(DateTimeOffset);
        var cursorId = default(Guid);
        var first = true;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isFirst = first;
            var capturedOccurredAt = cursorOccurredAt;
            var capturedId = cursorId;

            var page = await context.AuditEvents
                .AsNoTracking()
                .Where(e => e.CompanyId == companyId)
                .Where(e => isFirst
                    || e.OccurredAt < capturedOccurredAt
                    || (e.OccurredAt == capturedOccurredAt && e.Id < capturedId))
                .OrderByDescending(e => e.OccurredAt)
                .ThenByDescending(e => e.Id)
                .Take(PageSize)
                .Select(e => new
                {
                    e.OccurredAt, e.EventType, e.EntityType, e.EntityId, e.EmployeeId,
                    e.ActorUserId, e.ActorEmployeeId, e.Summary, e.Id
                })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (page.Count == 0)
                yield break;

            foreach (var r in page)
            {
                yield return new string?[]
                {
                    r.OccurredAt.ToString("o", CultureInfo.InvariantCulture),
                    r.EventType,
                    r.EntityType,
                    r.EntityId == Guid.Empty ? null : r.EntityId.ToString(),
                    r.EmployeeId?.ToString(),
                    r.ActorUserId?.ToString(),
                    r.ActorEmployeeId?.ToString(),
                    r.Summary
                };
            }

            if (page.Count < PageSize)
                yield break;

            var last = page[^1];
            cursorOccurredAt = last.OccurredAt;
            cursorId = last.Id;
            first = false;
        }
    }
}
