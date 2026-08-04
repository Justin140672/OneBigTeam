namespace HR.Modules.Support.Domain;

internal sealed class SupportRequest
{
    private SupportRequest() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SubmittedByUserId { get; private set; }
    public Guid? SubmittedByEmployeeId { get; private set; }
    public SupportRequestType Type { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public SupportRequestPriority Priority { get; private set; }
    public SupportRequestStatus Status { get; private set; }
    public string ReferenceNumber { get; private set; } = string.Empty;
    public string? PageUrl { get; private set; }
    public string? Browser { get; private set; }
    public string? AppVersion { get; private set; }
    public bool IncludeDiagnostics { get; private set; }
    public string? DiagnosticsJson { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static SupportRequest Create(
        Guid id,
        Guid companyId,
        Guid submittedByUserId,
        Guid? submittedByEmployeeId,
        SupportRequestType type,
        string title,
        string description,
        SupportRequestPriority priority,
        string referenceNumber,
        string? pageUrl,
        string? browser,
        string? appVersion,
        bool includeDiagnostics,
        string? diagnosticsJson,
        string? correlationId,
        DateTimeOffset now)
    {
        return new SupportRequest
        {
            Id = id,
            CompanyId = companyId,
            SubmittedByUserId = submittedByUserId,
            SubmittedByEmployeeId = submittedByEmployeeId,
            Type = type,
            Title = title,
            Description = description,
            Priority = priority,
            Status = SupportRequestStatus.Submitted,
            ReferenceNumber = referenceNumber,
            PageUrl = pageUrl,
            Browser = browser,
            AppVersion = appVersion,
            IncludeDiagnostics = includeDiagnostics,
            DiagnosticsJson = diagnosticsJson,
            CorrelationId = correlationId,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Applies a staff-driven status transition. Pragmatic guardrail: a closed request cannot be
    /// silently reopened back to Submitted (it must go through UnderReview or a similar active state).
    /// </summary>
    public bool CanTransitionTo(SupportRequestStatus newStatus)
    {
        if (Status == SupportRequestStatus.Closed && newStatus == SupportRequestStatus.Submitted)
            return false;

        return true;
    }

    public void ChangeStatus(SupportRequestStatus newStatus, DateTimeOffset now)
    {
        Status = newStatus;
        UpdatedAt = now;
    }

    public void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
    }
}
