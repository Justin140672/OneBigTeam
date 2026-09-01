namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Reporting-owned contract that lets the Infrastructure background job read and update a single
/// OrganisationDataExport row without touching ReportingDbContext directly. Implemented by an
/// internal service in HR.Modules.Reporting, DI-registered in ReportingModule.
/// </summary>
public interface IOrganisationDataExportJobStore
{
    Task<OrganisationDataExportJobView?> GetAsync(Guid exportId, CancellationToken cancellationToken);

    Task MarkInProgressAsync(Guid exportId, CancellationToken cancellationToken);

    Task MarkCompletedAsync(Guid exportId, string storageKey, long fileSizeBytes, CancellationToken cancellationToken);

    Task MarkFailedAsync(Guid exportId, string failureReason, CancellationToken cancellationToken);

    /// <summary>Completed exports past their expiry, for the recurring purge job.</summary>
    Task<IReadOnlyList<OrganisationDataExportJobView>> GetExpiredAsync(CancellationToken cancellationToken);

    Task MarkExpiredAsync(Guid exportId, CancellationToken cancellationToken);
}

public sealed record OrganisationDataExportJobView(
    Guid Id,
    Guid CompanyId,
    string Status,
    string? StorageKey,
    DateTimeOffset? ExpiresAt);
