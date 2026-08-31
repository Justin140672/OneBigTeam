using HR.SharedKernel;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>Shared audit-row builder for the ADM-08 governance audit report handler tests.</summary>
internal static class GovernanceAuditTestData
{
    public static AuditHistoryEntry Entry(
        string eventType,
        Guid? actorUserId = null,
        DateTimeOffset? occurredAt = null,
        Guid? employeeId = null,
        string entityType = "Company",
        string? summary = "summary")
        => new(
            occurredAt ?? new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
            eventType,
            entityType,
            actorUserId,
            null,
            summary,
            null,
            null,
            employeeId);
}
