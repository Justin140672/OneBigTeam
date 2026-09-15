using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Assets;
using HR.Modules.Assets.Features.CreateAsset;
using HR.Modules.Assets.Persistence;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Identity.Domain;
using HR.SharedKernel.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 3 (P1) final gap item 5: proves CreateAssetHandler's two
/// <see cref="IPostCommitFaultInjector"/> call sites behave as their comments claim, using
/// <see cref="FaultInjectingPostCommitFaultInjector"/> (armed via
/// <see cref="ApiWebApplicationFactory.PostCommitFaultInjector"/>) to simulate each failure exactly
/// once for a specific Idempotency-Key. Complements (does not duplicate)
/// CreateAssetIdempotencyIntegrationTests, which covers the replay/conflict/concurrency contract
/// without ever forcing a mid-flight failure.
/// </summary>
[Collection("Integration")]
public class CreateAssetIdempotencyFaultTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("cc000003-0000-0000-0000-000000000099");

    public CreateAssetIdempotencyFaultTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
        _factory.PostCommitFaultInjector.Reset();
    }

    [Fact]
    public async Task Post_CreateAsset_PostCommitFault_Persists_Row_And_Replays_On_Retry()
    {
        var (companyId, categoryId, client) = await SetupAutomaticNumberingCompanyAsync();
        var idempotencyKey = Guid.NewGuid();
        var payload = CreateAssetPayload(companyId, categoryId);

        // Arm the double to fail AFTER the handler's own commit (operation name has no
        // ".PreCommit" suffix) for this exact key.
        _factory.PostCommitFaultInjector.ArmOnce(nameof(CreateAssetHandler), idempotencyKey.ToString());

        var firstResponse = await SendCreateAsync(client, companyId, payload, idempotencyKey);
        Assert.True((int)firstResponse.StatusCode >= 500);

        // The business write must have already committed despite the "failed" response.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            Assert.Single(await db.Assets.Where(a => a.CompanyId == companyId).ToListAsync());
        }

        // A retry with the SAME key and payload must replay the stored response rather than create
        // a second asset row.
        var secondResponse = await SendCreateAsync(client, companyId, payload, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            Assert.Single(await db.Assets.Where(a => a.CompanyId == companyId).ToListAsync());
        }
    }

    [Fact]
    public async Task Post_CreateAsset_PreCommitFault_Persists_Nothing_And_Retry_Commits_Exactly_Once()
    {
        var (companyId, categoryId, client) = await SetupAutomaticNumberingCompanyAsync();
        var idempotencyKey = Guid.NewGuid();
        var payload = CreateAssetPayload(companyId, categoryId);

        // Arm the double to fail BEFORE anything commits for this exact key.
        _factory.PostCommitFaultInjector.ArmOnce($"{nameof(CreateAssetHandler)}.PreCommit", idempotencyKey.ToString());

        var firstResponse = await SendCreateAsync(client, companyId, payload, idempotencyKey);
        Assert.True((int)firstResponse.StatusCode >= 500);

        // Nothing committed on the failed first attempt — including no asset number consumed.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            Assert.Empty(await db.Assets.Where(a => a.CompanyId == companyId).ToListAsync());
        }

        // Retry with the same key now goes through cleanly and commits exactly once.
        var secondResponse = await SendCreateAsync(client, companyId, payload, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            Assert.Single(await db.Assets.Where(a => a.CompanyId == companyId).ToListAsync());
        }
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

    private static object CreateAssetPayload(Guid companyId, Guid categoryId) => new
    {
        companyId,
        // No assetNumber - the company is in Automatic mode, so the handler generates one.
        categoryId,
        name = "Fault Injection Test Laptop",
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

            settings.UpdateAssetNumberSettings(AssetNumberMode.Automatic, "FLT-", 1, 4, now);
            await db.SaveChangesAsync();
        }

        var categoryResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories", new { companyId, name = "Fault Injection Category" });
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<IdPayload>();

        return (companyId, category!.Id, client);
    }

    private sealed record IdPayload(Guid Id);
}
