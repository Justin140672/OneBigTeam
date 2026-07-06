namespace HR.Modules.Recruitment.Domain;

internal sealed class Vacancy
{
    private Vacancy() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? DepartmentId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Location { get; private set; }
    public VacancyStatus Status { get; private set; }
    public Guid HiringManagerId { get; private set; }
    public DateOnly? OpenedAt { get; private set; }
    public DateOnly? ClosedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Vacancy Create(
        Guid id,
        Guid companyId,
        Guid? departmentId,
        string title,
        string? description,
        string? location,
        Guid hiringManagerId,
        DateTimeOffset now) => new()
    {
        Id              = id,
        CompanyId       = companyId,
        DepartmentId    = departmentId,
        Title           = title.Trim(),
        Description     = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
        Location        = string.IsNullOrWhiteSpace(location) ? null : location.Trim(),
        Status          = VacancyStatus.Draft,
        HiringManagerId = hiringManagerId,
        CreatedAt       = now,
        UpdatedAt       = now,
    };

    public void Open(DateTimeOffset now, DateOnly openedAt)
    {
        if (Status is not (VacancyStatus.Draft or VacancyStatus.OnHold))
            throw new InvalidOperationException($"Cannot open a vacancy with status '{Status}'.");

        Status    = VacancyStatus.Open;
        OpenedAt  ??= openedAt;
        UpdatedAt = now;
    }

    public void Hold(DateTimeOffset now)
    {
        if (Status != VacancyStatus.Open)
            throw new InvalidOperationException($"Cannot put a vacancy with status '{Status}' on hold.");

        Status    = VacancyStatus.OnHold;
        UpdatedAt = now;
    }

    public void Close(DateTimeOffset now, DateOnly closedAt)
    {
        if (Status is VacancyStatus.Closed or VacancyStatus.Cancelled)
            throw new InvalidOperationException($"Cannot close a vacancy with status '{Status}'.");

        Status    = VacancyStatus.Closed;
        ClosedAt  = closedAt;
        UpdatedAt = now;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is VacancyStatus.Closed or VacancyStatus.Cancelled)
            throw new InvalidOperationException($"Cannot cancel a vacancy with status '{Status}'.");

        Status    = VacancyStatus.Cancelled;
        UpdatedAt = now;
    }
}
