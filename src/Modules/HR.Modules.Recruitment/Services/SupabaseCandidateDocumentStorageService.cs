using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Services;

/// <summary>
/// Reliability review issue 2 (P1): durable, production-grade candidate document storage backed by
/// Supabase Storage — mirrors HR.Modules.Documents.Services.SupabaseDocumentStorageService and
/// HR.Infrastructure.Storage.SupabaseSupportAttachmentStorageService. Replaces the local temp-directory
/// fallback (LocalCandidateDocumentStorageService) in every environment where Supabase storage is
/// configured; see RecruitmentModule.AddCandidateDocumentStorage for the environment-gating rule that
/// requires this in Staging/Production.
/// </summary>
internal sealed class SupabaseCandidateDocumentStorageService : ICandidateDocumentStorageService
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseCandidateDocumentStorageOptions _options;

    public SupabaseCandidateDocumentStorageService(
        HttpClient httpClient, IOptions<SupabaseCandidateDocumentStorageOptions> options)
    {
        _httpClient = httpClient;
        _options    = options.Value;
    }

    // The caller-supplied file name is untrusted and never embedded in the storage key (path
    // traversal / header-injection surface); only a random id and the file's own extension are
    // used, matching the Documents/Support-Attachments Supabase storage services.
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

    private sealed record SignedUrlResponse(
        [property: JsonPropertyName("signedURL")] string SignedUrl);
}
