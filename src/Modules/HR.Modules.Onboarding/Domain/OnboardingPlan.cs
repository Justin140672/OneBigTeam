namespace HR.Modules.Onboarding.Domain;

internal sealed class OnboardingPlan
{
    private OnboardingPlan() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public OnboardingStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static OnboardingPlan Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        DateOnly startDate,
        string? notes,
        DateTimeOffset now)
    {
        return new OnboardingPlan
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            StartDate = startDate,
            Status = OnboardingStatus.NotStarted,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Start(DateTimeOffset now)
    {
        Status = OnboardingStatus.InProgress;
        UpdatedAt = now;
    }

    public void Complete(DateTimeOffset now)
    {
        Status = OnboardingStatus.Completed;
        UpdatedAt = now;
    }

    public void Cancel(string? notes, DateTimeOffset now)
    {
        Status = OnboardingStatus.Cancelled;
        Notes = notes;
        UpdatedAt = now;
    }
}
