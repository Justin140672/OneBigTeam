namespace HR.Modules.Reporting.Domain;

internal sealed class ReportFavourite
{
    private ReportFavourite() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public string ReportId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static ReportFavourite Create(
        Guid id,
        Guid companyId,
        Guid userId,
        string reportId,
        DateTimeOffset now)
    {
        return new ReportFavourite
        {
            Id = id,
            CompanyId = companyId,
            UserId = userId,
            ReportId = reportId,
            CreatedAt = now,
        };
    }
}
