using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Storage;

internal sealed class SupabaseProfilePhotoStorageService : IProfilePhotoStorageService
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseProfilePhotoStorageOptions _options;

    public SupabaseProfilePhotoStorageService(HttpClient httpClient, IOptions<SupabaseProfilePhotoStorageOptions> options)
    {
        _httpClient = httpClient;
        _options    = options.Value;
    }

    public async Task<string> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        string storageFolder,
        CancellationToken cancellationToken)
    {
        var storageKey = $"{storageFolder.Trim('/')}/{Guid.NewGuid():N}/{fileName}";

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_options.SupabaseUrl}/storage/v1/object/{_options.BucketName}/{storageKey}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);
        request.Headers.Add("x-upsert", "false");
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return storageKey;
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

        // Supabase returns a path-only signedURL; prepend the project URL to make it absolute
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
