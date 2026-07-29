namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Company-wide (optionally position-profile-filtered) document compliance data for the Document
/// Compliance Report (OBT-714), as owned by HR.Modules.Documents. One row per employee who has a
/// position profile assigned — employees with no position profile have no required-document set
/// to compare against and are omitted.
/// </summary>
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
