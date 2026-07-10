using HR.Infrastructure.Abstractions;

namespace HR.Modules.Onboarding.Domain;

internal sealed class OnboardingTask
{
    private OnboardingTask() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid OnboardingPlanId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public OnboardingTemplateTaskAssignTo AssignTo { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public OnboardingTaskStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static OnboardingTask Create(
        Guid id,
        Guid companyId,
        Guid onboardingPlanId,
        string title,
        string? description,
        OnboardingTemplateTaskAssignTo assignTo,
        DateOnly? dueDate,
        DateTimeOffset now)
    {
        return new OnboardingTask
        {
            Id = id,
            CompanyId = companyId,
            OnboardingPlanId = onboardingPlanId,
            Title = title,
            Description = description,
            AssignTo = assignTo,
            DueDate = dueDate,
            Status = OnboardingTaskStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Complete(DateTimeOffset now)
    {
        Status = OnboardingTaskStatus.Completed;
        UpdatedAt = now;
    }

    public void Skip(DateTimeOffset now)
    {
        Status = OnboardingTaskStatus.Skipped;
        UpdatedAt = now;
    }
}
