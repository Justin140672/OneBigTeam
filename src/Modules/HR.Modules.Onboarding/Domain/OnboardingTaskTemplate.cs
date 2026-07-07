namespace HR.Modules.Onboarding.Domain;

internal sealed class OnboardingTaskTemplate
{
    private OnboardingTaskTemplate() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public int? DefaultDueDayOffset { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static OnboardingTaskTemplate Create(
        Guid id,
        Guid companyId,
        string title,
        string? description,
        int? defaultDueDayOffset,
        DateTimeOffset now)
    {
        return new OnboardingTaskTemplate
        {
            Id = id,
            CompanyId = companyId,
            Title = title,
            Description = description,
            DefaultDueDayOffset = defaultDueDayOffset,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(
        string title,
        string? description,
        int? defaultDueDayOffset,
        DateTimeOffset now)
    {
        Title = title;
        Description = description;
        DefaultDueDayOffset = defaultDueDayOffset;
        UpdatedAt = now;
    }
}
