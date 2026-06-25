namespace HR.Modules.Notifications.Domain;

internal sealed class Notification
{
    private Notification() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Body { get; private set; }
    public bool IsRead { get; private set; }
    public Guid SourceEntityId { get; private set; }
    public NotificationType Type { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static Notification Create(
        Guid id, Guid companyId, Guid employeeId,
        string title, string? body,
        Guid sourceEntityId, DateTimeOffset now,
        NotificationType type = NotificationType.TaskAssigned) => new()
    {
        Id             = id,
        CompanyId      = companyId,
        EmployeeId     = employeeId,
        Title          = title,
        Body           = body,
        IsRead         = false,
        SourceEntityId = sourceEntityId,
        Type           = type,
        CreatedAt      = now,
    };

    public void MarkAsRead() => IsRead = true;
}
