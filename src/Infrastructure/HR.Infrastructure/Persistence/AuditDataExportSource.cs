using System.Globalization;
using HR.Infrastructure.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Persistence;

/// <summary>
/// Story 2: contributes the company's committed audit-log rows to the organisation data export.
/// Implemented here (not in a module) because audit persistence is a cross-cutting infrastructure
/// capability. Rows are already redacted by the audit scrubber before they reach the committed
/// table. company_id is enforced.
/// </summary>
internal sealed class AuditDataExportSource(AuditDbContext context) : IAuditDataExportSource
{
    public async Task<IReadOnlyList<DataExportTable>> GetTablesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var rows = await context.AuditEvents
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId)
            .OrderByDescending(e => e.OccurredAt)
            .Select(e => new
            {
                e.OccurredAt, e.EventType, e.EntityType, e.EntityId, e.EmployeeId,
                e.ActorUserId, e.ActorEmployeeId, e.Summary
            })
            .ToListAsync(cancellationToken);

        var table = new DataExportTable(
            "audit_log",
            ["OccurredAt", "EventType", "EntityType", "EntityId", "EmployeeId", "ActorUserId", "ActorEmployeeId", "Summary"],
            rows.Select(r => (IReadOnlyList<string?>)new string?[]
            {
                r.OccurredAt.ToString("o", CultureInfo.InvariantCulture),
                r.EventType, r.EntityType, r.EntityId == Guid.Empty ? null : r.EntityId.ToString(),
                r.EmployeeId?.ToString(), r.ActorUserId?.ToString(), r.ActorEmployeeId?.ToString(), r.Summary
            }).ToList());

        return [table];
    }
}
