using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HR.Modules.DataImport.Services;

/// <summary>
/// Reliability review issue 2 (P1): durable, production-grade import file storage backed by Supabase
/// Storage — mirrors HR.Modules.Documents.Services.SupabaseDocumentStorageService. Replaces the local
/// temp-directory fallback (LocalImportFileStorageService) in every environment where Supabase
/// storage is configured. Durability matters here specifically because import validation/confirmation
/// happens in a separate request after upload — a process restart between stages must not orphan an
/// otherwise-valid import; see DataImportModule.AddImportFileStorage for the environment-gating rule
/// that requires this in Staging/Production.
/// </summary>
internal sealed class SupabaseImportFileStorageService : IImportFileStorageService
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseImportFileStorageOptions _options;

    public SupabaseImportFileStorageService(
        HttpClient httpClient, IOptions<SupabaseImportFileStorageOptions> options)
    {
        _httpClient = httpClient;
        _options    = options.Value;
    }

    public string GenerateStorageKey(string storageFolder, string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return $"{storageFolder.Trim('/')}/{Guid.NewGuid():N}{extension}";
    }

    public async Task UploadAsync(
        Stream content,
        string storageKey,
        string contentType,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.SupabaseUrl}/storage/v1/object/{_options.BucketName}/{storageKey}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        request.Headers.Add("x-upsert", "false");
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<bool> ExistsAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Head,
            $"{_options.SupabaseUrl}/storage/v1/object/{_options.BucketName}/{storageKey}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<Uri> GetDownloadUrlAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.SupabaseUrl}/storage/v1/object/sign/{_options.BucketName}/{storageKey}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        request.Content = JsonContent.Create(new { expiresIn = _options.SignedUrlExpirySeconds });

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SignedUrlResponse>(cancellationToken: cancellationToken);

        var signedUrl = result!.SignedUrl;
        return signedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new Uri(signedUrl)
            : new Uri($"{_options.SupabaseUrl.TrimEnd('/')}{signedUrl}");
    }

    public async Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{_options.SupabaseUrl}/storage/v1/object/{_options.BucketName}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        request.Content = JsonContent.Create(new { prefixes = new[] { storageKey } });

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.SupabaseUrl}/storage/v1/object/{_options.BucketName}/{storageKey}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);

        var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    private sealed record SignedUrlResponse(
        [property: JsonPropertyName("signedURL")] string SignedUrl);
}
