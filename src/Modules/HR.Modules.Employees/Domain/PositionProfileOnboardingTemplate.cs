namespace HR.Modules.Employees.Domain;

internal sealed class PositionProfileOnboardingTemplate
{
    private PositionProfileOnboardingTemplate() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PositionProfileId { get; private set; }
    public Guid OnboardingTemplateId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }

    public void Deactivate()
    {
        IsActive = false;
    }

    public static PositionProfileOnboardingTemplate Create(
        Guid id,
        Guid companyId,
        Guid positionProfileId,
        Guid onboardingTemplateId,
        Guid createdBy,
        DateTimeOffset now)
    {
        return new PositionProfileOnboardingTemplate
        {
            Id = id,
            CompanyId = companyId,
            PositionProfileId = positionProfileId,
            OnboardingTemplateId = onboardingTemplateId,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }
}
