using System.Net.Http.Json;

using HR.Admin.Web.Models;

namespace HR.Admin.Web.Services;

/// <summary>
/// Wraps the platform-admin marketing content endpoints (<c>/api/marketing/admin/*</c>) exposed by
/// HR.Modules.Marketing. Modeled on <see cref="PlatformSettingsService"/>: "hrapi" HttpClientFactory
/// client, null on read failure, 422 FluentValidation field errors surfaced on writes.
/// </summary>
public sealed class MarketingContentAdminService(IHttpClientFactory httpClientFactory)
{
    private HttpClient Http => httpClientFactory.CreateClient("hrapi");

    public async Task<AdminMarketingContentModel?> GetContentOrNullAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await Http.GetAsync("api/marketing/admin/content", cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<AdminMarketingContentModel>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public Task<MarketingMutationResult> CreateFeatureAsync(CreateMarketingFeatureRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/marketing/admin/features", request, ct);

    public Task<MarketingMutationResult> UpdateFeatureAsync(UpdateMarketingFeatureRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/marketing/admin/features/{request.Id}", request, ct);

    public Task<MarketingMutationResult> SetFeaturePublicationAsync(Guid id, bool isPublished, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/marketing/admin/features/{id}/publication", new { Id = id, IsPublished = isPublished }, ct);

    public Task<MarketingMutationResult> ReorderFeaturesAsync(IReadOnlyList<Guid> orderedIds, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, "api/marketing/admin/features/order", new { OrderedIds = orderedIds }, ct);

    public Task<MarketingMutationResult> CreateRoadmapItemAsync(CreateMarketingRoadmapItemRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/marketing/admin/roadmap", request, ct);

    public Task<MarketingMutationResult> UpdateRoadmapItemAsync(UpdateMarketingRoadmapItemRequest request, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/marketing/admin/roadmap/{request.Id}", request, ct);

    public Task<MarketingMutationResult> SetRoadmapItemPublicationAsync(Guid id, bool isPublished, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, $"api/marketing/admin/roadmap/{id}/publication", new { Id = id, IsPublished = isPublished }, ct);

    public Task<MarketingMutationResult> ReorderRoadmapItemsAsync(IReadOnlyList<Guid> orderedIds, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, "api/marketing/admin/roadmap/order", new { OrderedIds = orderedIds }, ct);

    private async Task<MarketingMutationResult> SendAsync(HttpMethod method, string uri, object body, CancellationToken ct)
    {
        try
        {
            using var message = new HttpRequestMessage(method, uri) { Content = JsonContent.Create(body) };
            var response = await Http.SendAsync(message, ct);

            if (response.IsSuccessStatusCode)
                return MarketingMutationResult.Success;

            if ((int)response.StatusCode == 422)
            {
                var envelope = await response.Content.ReadFromJsonAsync<ValidationErrorEnvelope>(cancellationToken: ct);
                var errors = envelope?.Errors?.Values.SelectMany(v => v).ToArray();
                if (errors is { Length: > 0 })
                    return MarketingMutationResult.Failure(errors);
            }

            if ((int)response.StatusCode == 409)
            {
                var problem = await SafeReadErrorAsync(response, ct);
                return MarketingMutationResult.Failure(problem ?? "That slug is already in use.");
            }

            if ((int)response.StatusCode is 401 or 403)
                return MarketingMutationResult.Failure("You're not authorised to manage marketing content.");

            if ((int)response.StatusCode == 404)
                return MarketingMutationResult.Failure("That item no longer exists. Reload the page.");

            var generic = await SafeReadErrorAsync(response, ct);
            return MarketingMutationResult.Failure(generic ?? "Could not save marketing content.");
        }
        catch (HttpRequestException)
        {
            return MarketingMutationResult.Failure("A network error occurred. Please try again.");
        }
    }

    private static async Task<string?> SafeReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<BusinessError>(cancellationToken: ct);
            return body?.Error;
        }
        catch
        {
            return null;
        }
    }

    private sealed record ValidationErrorEnvelope(Dictionary<string, string[]>? Errors);

    private sealed record BusinessError(string? Error);
}
