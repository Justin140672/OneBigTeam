namespace HR.Infrastructure.Abstractions;

public interface IDocumentComplianceReportReader
{
    Task<IReadOnlyList<DocumentComplianceReportItem>> GetDocumentComplianceReportAsync(
        Guid companyId,
        Guid? positionProfileId,
        CancellationToken cancellationToken);
}

public sealed record DocumentComplianceReportItem(
    Guid EmployeeId,
    Guid? PositionProfileId,
    int RequiredCount,
    int UploadedCount,
    int MissingCount,
    int ExpiringSoonCount,
    int ExpiredCount,
    IReadOnlyList<string> MissingDocumentTypeNames);
