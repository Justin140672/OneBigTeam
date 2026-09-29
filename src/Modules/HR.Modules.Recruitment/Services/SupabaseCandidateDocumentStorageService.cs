using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace HR.Modules.Recruitment.Services;

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

    public async Task<Stream> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_options.SupabaseUrl}/storage/v1/object/authenticated/{_options.BucketName}/{storageKey}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ServiceRoleKey);

        using var response = await _httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var buffer = new MemoryStream();
        await using (var content = await response.Content.ReadAsStreamAsync(cancellationToken))
        {
            await content.CopyToAsync(buffer, cancellationToken);
        }

        buffer.Position = 0;
        return buffer;
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
