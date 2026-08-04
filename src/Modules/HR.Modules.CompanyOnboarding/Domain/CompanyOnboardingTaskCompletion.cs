namespace HR.Modules.CompanyOnboarding.Domain;

internal sealed class CompanyOnboardingTaskCompletion
{
    private CompanyOnboardingTaskCompletion() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string TaskKey { get; private set; } = string.Empty;
    public bool IsCompleted { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CompanyOnboardingTaskCompletion Create(Guid id, Guid companyId, string taskKey, DateTimeOffset now)
    {
        return new CompanyOnboardingTaskCompletion
        {
            Id = id,
            CompanyId = companyId,
            TaskKey = taskKey,
            IsCompleted = false,
            CompletedAt = null,
            UpdatedAt = now,
        };
    }

    public void SetStatus(bool isCompleted, DateTimeOffset now)
    {
        IsCompleted = isCompleted;
        CompletedAt = isCompleted ? (CompletedAt ?? now) : null;
        UpdatedAt = now;
    }
}
