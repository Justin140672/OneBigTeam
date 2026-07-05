namespace HR.SharedKernel;

public interface IAuditHistoryReader
{
    Task<IReadOnlyList<AuditHistoryEntry>> GetEmployeeAuditHistoryAsync(
        Guid companyId, Guid employeeId, CancellationToken cancellationToken);
}

public sealed record AuditHistoryEntry(
    DateTimeOffset OccurredAt,
    string EventType,
    string EntityType,
    Guid? ActorUserId,
    Guid? ActorEmployeeId,
    string? Summary,
    string? BeforeJson,
    string? AfterJson);
