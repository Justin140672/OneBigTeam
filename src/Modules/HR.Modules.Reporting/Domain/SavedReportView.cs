namespace HR.Modules.Reporting.Domain;

internal sealed class SavedReportView
{
    private SavedReportView() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public string ReportId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string FilterCriteriaJson { get; private set; } = string.Empty;
    public bool IsDefault { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static SavedReportView Create(
        Guid id,
        Guid companyId,
        Guid userId,
        string reportId,
        string name,
        string filterCriteriaJson,
        bool isDefault,
        DateTimeOffset now)
    {
        return new SavedReportView
        {
            Id = id,
            CompanyId = companyId,
            UserId = userId,
            ReportId = reportId,
            Name = name,
            FilterCriteriaJson = filterCriteriaJson,
            IsDefault = isDefault,
            CreatedAt = now,
        };
    }

    public void Rename(string name)
    {
        Name = name;
    }

    public void SetIsDefault(bool isDefault)
    {
        IsDefault = isDefault;
    }
}
