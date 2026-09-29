namespace HR.Modules.Employees.Domain;

internal sealed class PendingManagerChangedEvent
{
    private PendingManagerChangedEvent() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ReportEmployeeId { get; private set; }
    public Guid? PreviousManagerId { get; private set; }
    public Guid? NewManagerId { get; private set; }
    public Guid LeavingProcessId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    public static PendingManagerChangedEvent Create(
        Guid id,
        Guid companyId,
        Guid reportEmployeeId,
        Guid? previousManagerId,
        Guid? newManagerId,
        Guid leavingProcessId,
        DateTimeOffset occurredAt,
        DateTimeOffset now)
    {
        return new PendingManagerChangedEvent
        {
            Id = id,
            CompanyId = companyId,
            ReportEmployeeId = reportEmployeeId,
            PreviousManagerId = previousManagerId,
            NewManagerId = newManagerId,
            LeavingProcessId = leavingProcessId,
            OccurredAt = occurredAt,
            CreatedAt = now,
        };
    }

    public void MarkPublished(DateTimeOffset now)
    {
        if (PublishedAt is not null)
            return;

        PublishedAt = now;
    }
}
