namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Reads all asset assignments (both active and returned) for a company, for reporting
/// purposes. Unlike <see cref="IAssignedAssetReader"/>, this is company-wide rather than
/// per-employee and includes return status, so it must not be repurposed for that use case.
/// </summary>
public interface IAssetAssignmentReportReader
{
    Task<IReadOnlyList<AssetAssignmentReportItem>> GetAssetAssignmentsAsync(
        Guid companyId,
        CancellationToken cancellationToken);
}

public sealed record AssetAssignmentReportItem(
    Guid AssetAssignmentId,
    Guid EmployeeId,
    string AssetName,
    string? SerialNumber,
    DateTimeOffset AssignedDate,
    string ReturnStatus);
