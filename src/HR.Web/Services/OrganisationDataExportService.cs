namespace HR.Web.Services;

/// <summary>
/// Story 2: HR.Web data service for the customer-facing organisation data export feature. Calls the
/// four Reporting endpoints under /api/companies/{companyId}/reporting/data-exports using the shared
/// "hrapi" IHttpClientFactory client (bearer auth is attached by the client's handler, same pattern
/// as ReportingService/SubscriptionService).
/// </summary>
public class OrganisationDataExportService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    private static string Base(Guid companyId) => $"api/companies/{companyId}/reporting/data-exports";

    /// <summary>POST — requests a new export. Returns (ExportId, Status) on success, an error message otherwise.</summary>
    public async Task<(RequestOrganisationDataExportResult? Result, string? Error)> RequestAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsync(Base(companyId), content: null, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<RequestOrganisationDataExportResult>(
                    HrApiJsonOptions.Default, cancellationToken);
                return (result, null);
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                return (null, "An export is already being prepared. Wait for it to finish before requesting another.");

            return (null, "Unable to request a data export. Please try again.");
        }
        catch (HttpRequestException)
        {
            return (null, "Unable to request a data export. Please try again.");
        }
    }

    /// <summary>GET latest — always returns a payload (Status null when no export has ever been requested).</summary>
    public async Task<OrganisationDataExportLatest?> GetLatestAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await Http.GetFromJsonAsync<OrganisationDataExportLatest>(
                $"{Base(companyId)}/latest", HrApiJsonOptions.Default, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>GET list — recent export history (newest first).</summary>
    public async Task<IReadOnlyList<OrganisationDataExportHistoryItem>> GetHistoryAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetFromJsonAsync<OrganisationDataExportHistory>(
                Base(companyId), HrApiJsonOptions.Default, cancellationToken);
            return response?.Exports ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    /// <summary>GET download — returns the ZIP bytes for a completed export, or an error message.</summary>
    public async Task<(byte[]? Bytes, string? ContentType, string? FileName, string? Error)> DownloadAsync(
        Guid companyId, Guid exportId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync($"{Base(companyId)}/{exportId}/download", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return (null, null, null, "This export is no longer available for download.");

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/zip";
            var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                ?? "organisation-data-export.zip";

            return (bytes, contentType, fileName, null);
        }
        catch (HttpRequestException)
        {
            return (null, null, null, "This export is no longer available for download.");
        }
    }
}

public sealed record RequestOrganisationDataExportResult(Guid ExportId, string Status);

public sealed record OrganisationDataExportLatest(
    Guid? ExportId,
    string? Status,
    DateTimeOffset? RequestedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    long? FileSizeBytes,
    bool Downloadable);

public sealed record OrganisationDataExportHistory(IReadOnlyList<OrganisationDataExportHistoryItem> Exports);

public sealed record OrganisationDataExportHistoryItem(
    Guid ExportId,
    string Status,
    DateTimeOffset RequestedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    long? FileSizeBytes,
    int DownloadCount,
    bool Downloadable);
