namespace HR.Modules.Reporting.Features.GetLatestOrganisationDataExport;

internal sealed record GetLatestOrganisationDataExportResponse(
    Guid? ExportId,
    string? Status,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    long? FileSizeBytes,
    bool Downloadable);
