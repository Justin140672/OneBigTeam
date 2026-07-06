using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Domain;

internal sealed class OnboardingTemplateTask
{
    private OnboardingTemplateTask() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid OnboardingTemplateId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public TaskPriority Priority { get; private set; }
    public OnboardingTemplateTaskAssignTo AssignTo { get; private set; }
    public int DueDaysAfterStart { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }

    public static OnboardingTemplateTask Create(
        Guid id,
        Guid companyId,
        Guid onboardingTemplateId,
        string title,
        string? description,
        TaskPriority priority,
        OnboardingTemplateTaskAssignTo assignTo,
        int dueDaysAfterStart,
        int displayOrder)
    {
        return new OnboardingTemplateTask
        {
            Id = id,
            CompanyId = companyId,
            OnboardingTemplateId = onboardingTemplateId,
            Title = title,
            Description = description,
            Priority = priority,
            AssignTo = assignTo,
            DueDaysAfterStart = dueDaysAfterStart,
            DisplayOrder = displayOrder,
            IsActive = true,
        };
    }

    public void Update(
        string title,
        string? description,
        TaskPriority priority,
        OnboardingTemplateTaskAssignTo assignTo,
        int dueDaysAfterStart,
        int displayOrder)
    {
        Title = title;
        Description = description;
        Priority = priority;
        AssignTo = assignTo;
        DueDaysAfterStart = dueDaysAfterStart;
        DisplayOrder = displayOrder;
    }

    public void Deactivate()
    {
        IsActive = false;
    }
}
