using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Assets.Persistence;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Identity.Domain;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Bug fix (P1 follow-up to Ticket 19): proves the full client-realistic path at the API/
/// idempotency-record level. The real fix lives client-side in AssetService.BuildRequestSnapshot -
/// it guarantees a whitespace-only edit (e.g. " Laptop " -> "Laptop") normalizes to the SAME
/// outgoing request body, so EditPageBase's PendingIdempotentOperation reuses the same idempotency
/// key for both. This test drives the API with exactly that scenario: same key, same normalized
/// body sent twice (mirroring what the fixed client now actually transmits over the wire), and
/// asserts the standard idempotent-replay outcome - exactly one asset, one allocated asset number,
/// one idempotency record, and one audit outbox entry. (CreateAssetIdempotencyIntegrationTests'
/// Reusing_A_Key_With_A_Changed_Payload_Returns_The_Documented_Conflict already proves the server
/// keys on the request body, not just the header, so a genuinely different body under a reused key
/// is rejected with 409 rather than silently treated as a replay - that path is not re-tested here.)
/// </summary>
[Collection("Integration")]
public class CreateAssetIdempotencyNormalizationIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("dd000003-0000-0000-0000-000000000199");

    public CreateAssetIdempotencyNormalizationIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Retrying_With_The_Normalized_Body_The_Fixed_Client_Would_Have_Sent_Replays_Instead_Of_Duplicating()
    {
        var (companyId, categoryId, client) = await SetupAutomaticNumberingCompanyAsync();
        var idempotencyKey = Guid.NewGuid();

        // The fixed client (AssetService.BuildRequestSnapshot) always applies FormText.Required
        // normalization BEFORE fingerprinting and BEFORE sending - so whether the user typed
        // " Laptop " or "Laptop" first, the request body actually transmitted over HTTP is always
        // the normalized "Laptop", under the same key. This test sends that normalized body twice,
        // simulating an ambiguous-retry-after-a-lost-response for that already-normalized request.
        var normalizedPayload = CreateAssetPayload(companyId, categoryId, "Laptop");

        var first = await SendCreateAsync(client, companyId, normalizedPayload, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<AssetPayload>();

        // Simulate losing that response and resending through a BRAND NEW HttpRequestMessage with
        // the same key and the identical (already-normalized) body.
        var second = await SendCreateAsync(client, companyId, normalizedPayload, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<AssetPayload>();

        Assert.Equal(firstBody!.Id, secondBody!.Id);
        Assert.Equal(firstBody.AssetNumber, secondBody.AssetNumber);
        Assert.Equal("Laptop", firstBody.Name);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();

        var assets = await db.Assets.Where(a => a.CompanyId == companyId).ToListAsync();
        Assert.Single(assets); // one asset, one asset number consumed - not two

        var idempotencyRows = await db.IdempotencyRecords
            .Where(r => r.Key == idempotencyKey.ToString() && r.CompanyId == companyId)
            .ToListAsync();
        Assert.Single(idempotencyRows);

        Assert.Single(await db.AuditOutboxEntries.Where(e => e.CompanyId == companyId).ToListAsync());
    }

    // ── Helpers (mirrors CreateAssetIdempotencyIntegrationTests' setup) ────────────

    private static Task<HttpResponseMessage> SendCreateAsync(
        HttpClient client, Guid companyId, object payload, Guid idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/companies/{companyId}/assets")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Add(IdempotentHttpClientExtensions.HeaderName, idempotencyKey.ToString());
        return client.SendAsync(request);
    }

    private static object CreateAssetPayload(Guid companyId, Guid categoryId, string name) => new
    {
        companyId,
        // No assetNumber - the company is in Automatic mode, so the handler generates one.
        categoryId,
        name,
    };

    private async Task<HttpClient> AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, AdminUserId, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private async Task<(Guid CompanyId, Guid CategoryId, HttpClient Client)> SetupAutomaticNumberingCompanyAsync()
    {
        var companyId = Guid.NewGuid();
        var client = await AdminClient(companyId);

        var now = DateTimeOffset.UtcNow;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
            var settings = await db.CompanySettings.SingleOrDefaultAsync(s => s.CompanyId == companyId);
            if (settings is null)
            {
                settings = CompanySettings.CreateDefault(companyId, now);
                db.CompanySettings.Add(settings);
            }

            settings.UpdateAssetNumberSettings(AssetNumberMode.Automatic, "NORM-", 1, 4, now);
            await db.SaveChangesAsync();
        }

        var categoryResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories", new { companyId, name = "Normalization Category" });
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<IdPayload>();

        return (companyId, category!.Id, client);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record AssetPayload(
        Guid Id, Guid CompanyId, string AssetNumber, Guid CategoryId, string Name,
        string? Manufacturer, string? Model, string? SerialNumber,
        DateOnly? PurchaseDate, decimal? PurchasePrice, string Status,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
