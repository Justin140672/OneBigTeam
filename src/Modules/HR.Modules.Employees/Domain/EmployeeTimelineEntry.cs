namespace HR.Modules.Employees.Domain;

internal sealed class EmployeeTimelineEntry
{
    private EmployeeTimelineEntry() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }

    public DateOnly EventDate { get; private set; }
    public EmployeeTimelineEventType EventType { get; private set; }
    public EmployeeTimelineCategory Category { get; private set; }
    public string Title { get; private set; } = null!;
    public string Summary { get; private set; } = null!;
    public Guid? PerformedByUserId { get; private set; }
    public string SourceModule { get; private set; } = null!;
    public Guid? SourceRecordId { get; private set; }
    public EmployeeTimelineVisibility Visibility { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }

    public DateTimeOffset? BackfilledAt { get; private set; }

    public static EmployeeTimelineEntry Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        DateOnly eventDate,
        EmployeeTimelineEventType eventType,
        EmployeeTimelineCategory category,
        string title,
        string summary,
        Guid? performedByUserId,
        string sourceModule,
        Guid? sourceRecordId,
        EmployeeTimelineVisibility visibility,
        DateTimeOffset now,
        DateTimeOffset? backfilledAt = null)
    {
        return new EmployeeTimelineEntry
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            EventDate = eventDate,
            EventType = eventType,
            Category = category,
            Title = title,
            Summary = summary,
            PerformedByUserId = performedByUserId,
            SourceModule = sourceModule,
            SourceRecordId = sourceRecordId,
            Visibility = visibility,
            CreatedDate = now,
            BackfilledAt = backfilledAt,
        };
    }
}
