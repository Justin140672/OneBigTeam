using System.Net.Http.Json;

using Microsoft.Extensions.Caching.Memory;

namespace HR.Marketing.Services;

/// <summary>
/// Managed marketing content (product features + roadmap) surfaced from the central
/// <c>HR.Modules.Marketing</c> database via the public <c>GET /api/marketing/content</c> endpoint,
/// which only ever returns published rows.
/// </summary>
public sealed record MarketingContent(
    string ProductName,
    string? ProductTagline,
    IReadOnlyList<MarketingFeatureContent> Features,
    IReadOnlyList<MarketingRoadmapContent> Roadmap);

public sealed record MarketingFeatureContent(
    string Slug,
    string IconName,
    string Title,
    string Summary,
    string Intro,
    string? DetailedContent,
    IReadOnlyList<string> Benefits,
    string? YouTubeId,
    string DeliveryStatus);

public sealed record MarketingRoadmapContent(
    string Title,
    string Description,
    string IconName,
    string DeliveryStatus);

/// <summary>
/// Typed client for the managed marketing content endpoint.
///
/// Resilience model (Ticket 3):
/// <list type="bullet">
///   <item>Successful responses are cached in <see cref="IMemoryCache"/> for
///     <see cref="CacheDuration"/> (5 minutes) so published edits appear without a redeploy,
///     and normal page loads never wait on the API.</item>
///   <item>The most recent successful response is also retained indefinitely in
///     <see cref="_lastKnownGood"/>. If the endpoint is unavailable when the cache is cold we
///     serve that copy rather than failing the request.</item>
///   <item>If we have never reached the endpoint, we fall back to the compiled-in
///     <see cref="FeatureCatalog"/> / <see cref="UpcomingFeatureCatalog"/> seed data. The
///     marketing site therefore never returns a 500 and never shows unpublished drafts.</item>
/// </list>
/// Registered as a singleton so <see cref="_lastKnownGood"/> survives across requests.
/// </summary>
public sealed class MarketingContentService(IHttpClientFactory httpClientFactory, IMemoryCache cache)
{
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private const string CacheKey = "marketing-content";

    private static readonly SemaphoreSlim RefreshLock = new(1, 1);

    private volatile MarketingContent? _lastKnownGood;

    public async Task<MarketingContent> GetContentAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out MarketingContent? cached) && cached is not null)
        {
            return cached;
        }

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(CacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var fetched = await TryFetchAsync(cancellationToken);
            if (fetched is not null)
            {
                _lastKnownGood = fetched;
                cache.Set(CacheKey, fetched, CacheDuration);
                return fetched;
            }

            // Endpoint unavailable — serve the last good copy, or the compiled-in seed data.
            return _lastKnownGood ?? SeedFallback;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    public async Task<MarketingFeatureContent?> GetFeatureAsync(string slug, CancellationToken cancellationToken = default)
    {
        var content = await GetContentAsync(cancellationToken);
        return content.Features.FirstOrDefault(f => string.Equals(f.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<MarketingContent?> TryFetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            var http = httpClientFactory.CreateClient("hrapi");
            var response = await http.GetAsync("api/marketing/content", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var dto = await response.Content.ReadFromJsonAsync<ContentResponse>(cancellationToken);
            if (dto is null)
            {
                return null;
            }

            return new MarketingContent(
                dto.Product?.Name ?? "One Big Team",
                dto.Product?.Tagline,
                (dto.Features ?? []).Select(f => new MarketingFeatureContent(
                    f.Slug, f.IconName, f.Title, f.Summary, f.Intro, f.DetailedContent,
                    f.Benefits ?? [], f.YouTubeId, f.DeliveryStatus ?? "Available")).ToList(),
                (dto.Roadmap ?? []).Select(r => new MarketingRoadmapContent(
                    r.Title, r.Description, r.IconName, r.DeliveryStatus ?? "Planned")).ToList());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static readonly MarketingContent SeedFallback = new(
        "One Big Team",
        null,
        FeatureCatalog.All.Select(f => new MarketingFeatureContent(
            f.Slug, f.IconName, f.Title, f.Summary, f.Intro, null, f.Benefits, f.YouTubeId, "Available")).ToList(),
        UpcomingFeatureCatalog.All.Select(r => new MarketingRoadmapContent(
            r.Title, r.Description, r.IconName, r.Status.ToString())).ToList());

    private sealed record ContentResponse(ProductNode? Product, List<FeatureNode>? Features, List<RoadmapNode>? Roadmap);

    private sealed record ProductNode(string? Name, string? Tagline);

    private sealed record FeatureNode(
        string Slug,
        string IconName,
        string Title,
        string Summary,
        string Intro,
        string? DetailedContent,
        List<string>? Benefits,
        string? YouTubeId,
        string? DeliveryStatus);

    private sealed record RoadmapNode(string Title, string Description, string IconName, string? DeliveryStatus);
}
