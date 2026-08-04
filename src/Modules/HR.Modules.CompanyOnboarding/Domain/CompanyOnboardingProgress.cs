namespace HR.Modules.CompanyOnboarding.Domain;

internal sealed class CompanyOnboardingProgress
{
    private CompanyOnboardingProgress() { }

    public Guid CompanyId { get; private set; }
    public bool IsDismissedEarly { get; private set; }
    public bool IsHidden { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static CompanyOnboardingProgress Create(Guid companyId, DateTimeOffset now)
    {
        return new CompanyOnboardingProgress
        {
            CompanyId = companyId,
            IsDismissedEarly = false,
            IsHidden = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void MarkDismissed(DateTimeOffset now)
    {
        IsDismissedEarly = true;
        IsHidden = true;
        UpdatedAt = now;
    }

    public void MarkCompleted(DateTimeOffset now)
    {
        CompletedAt ??= now;
        IsHidden = true;
        UpdatedAt = now;
    }
}
