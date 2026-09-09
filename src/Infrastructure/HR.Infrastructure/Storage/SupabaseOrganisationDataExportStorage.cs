using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Storage;

/// <summary>
/// Hosted implementation of <see cref="IOrganisationDataExportStorage"/> backed by Supabase
/// Storage, mirroring <see cref="SupabaseSupportAttachmentStorageService"/>. Key convention:
/// organisation-exports/{companyId}/{exportId}.zip.
/// </summary>
internal sealed class SupabaseOrganisationDataExportStorage : IOrganisationDataExportStorage
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseOrganisationDataExportStorageOptions _options;

    public SupabaseOrganisationDataExportStorage(
        HttpClient httpClient,
        IOptions<SupabaseOrganisationDataExportStorageOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> UploadAsync(Guid companyId, Guid exportId, Guid attemptToken, Stream content, CancellationToken cancellationToken)
    {
        var storageKey = $"organisation-exports/{companyId}/{exportId}/{attemptToken}.zip";

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.SupabaseUrl}/storage/v1/object/{_options.BucketName}/{storageKey}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        request.Headers.Add("x-upsert", "true");
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return storageKey;
    }

    public async Task<Stream?> OpenAsync(string storageKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.SupabaseUrl}/storage/v1/object/{_options.BucketName}/{storageKey}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        return memory;
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{_options.SupabaseUrl}/storage/v1/object/{_options.BucketName}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        request.Content = System.Net.Http.Json.JsonContent.Create(new { prefixes = new[] { storageKey } });

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;

        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<string>> ListAttemptKeysAsync(Guid companyId, Guid exportId, CancellationToken cancellationToken)
    {
        var folder = $"organisation-exports/{companyId}/{exportId}";

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.SupabaseUrl}/storage/v1/object/list/{_options.BucketName}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        request.Content = System.Net.Http.Json.JsonContent.Create(new
        {
            prefix = $"{folder}/",
            limit = 1000,
        });

        var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];

        response.EnsureSuccessStatusCode();

        var items = await response.Content.ReadFromJsonAsync<List<SupabaseStorageObject>>(cancellationToken);
        if (items is null)
            return [];

        return items
            .Where(i => !string.IsNullOrWhiteSpace(i.Name))
            .Select(i => $"{folder}/{i.Name}")
            .ToList();
    }

    private sealed record SupabaseStorageObject(string? Name);
}
