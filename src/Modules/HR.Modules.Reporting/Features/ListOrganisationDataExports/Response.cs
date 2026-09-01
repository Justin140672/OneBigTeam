namespace HR.Modules.Reporting.Features.ListOrganisationDataExports;

internal sealed record ListOrganisationDataExportsResponse(IReadOnlyList<OrganisationDataExportListItem> Exports);

internal sealed record OrganisationDataExportListItem(
    Guid ExportId,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    long? FileSizeBytes,
    int DownloadCount,
    bool Downloadable);
