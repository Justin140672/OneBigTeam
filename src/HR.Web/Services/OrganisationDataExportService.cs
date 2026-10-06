namespace HR.Web.Services;

public class OrganisationDataExportService(HrApiHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient();

    private static string Base(Guid companyId) => $"api/companies/{companyId}/reporting/data-exports";

    public async Task<(RequestOrganisationDataExportResult? Result, string? Error)> RequestAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.PostAsJsonAsync(Base(companyId), new { }, cancellationToken);

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

    public static string DownloadUrl(Guid companyId, Guid exportId) =>
        $"/companies/{companyId}/organisation-data-exports/{exportId}/download";
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
    bool Downloadable)
{
    public string StatusText => HR.SharedKernel.EnumText.Humanize(Status);
}
