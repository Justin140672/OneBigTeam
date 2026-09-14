using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Assets;
using HR.Modules.Assets.Persistence;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using HR.Modules.Identity.Domain;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using HR.SharedKernel.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 3 (P1) final follow-up item 5: proves the automatic-asset-creation idempotency contract
/// against a real PostgreSQL instance - the concurrent-race scenario specifically needs a real
/// unique-constraint violation, which EF's InMemory provider cannot produce.
/// </summary>
[Collection("Integration")]
public class CreateAssetIdempotencyIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("bb000003-0000-0000-0000-000000000199");

    public CreateAssetIdempotencyIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () => await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Replaying_The_Same_Key_After_A_Lost_Response_Creates_One_Asset_And_Consumes_One_Number()
    {
        var (companyId, categoryId, client) = await SetupAutomaticNumberingCompanyAsync();
        var idempotencyKey = Guid.NewGuid();
        var payload = CreateAssetPayload(companyId, categoryId);

        var first = await SendCreateAsync(client, companyId, payload, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<AssetPayload>();

        // Simulate losing that response and resending through a BRAND NEW HttpRequestMessage with
        // the same key.
        var second = await SendCreateAsync(client, companyId, payload, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<AssetPayload>();

        Assert.Equal(firstBody!.Id, secondBody!.Id);
        Assert.Equal(firstBody.AssetNumber, secondBody.AssetNumber);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();

        var assets = await db.Assets.Where(a => a.CompanyId == companyId).ToListAsync();
        Assert.Single(assets); // one asset, one number consumed - not two

        var idempotencyRows = await db.IdempotencyRecords
            .Where(r => r.Key == idempotencyKey.ToString() && r.CompanyId == companyId)
            .ToListAsync();
        Assert.Single(idempotencyRows);

        Assert.Single(await db.AuditOutboxEntries.Where(e => e.CompanyId == companyId).ToListAsync());
    }

    [Fact]
    public async Task Reusing_A_Key_With_A_Changed_Payload_Returns_The_Documented_Conflict()
    {
        // Ticket 3 (P1) final follow-up item 6: proves the full client-to-API contract for a
        // changed request under a reused key.
        var (companyId, categoryId, client) = await SetupAutomaticNumberingCompanyAsync();
        var idempotencyKey = Guid.NewGuid();

        var first = await SendCreateAsync(client, companyId, CreateAssetPayload(companyId, categoryId), idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var changedPayload = new
        {
            companyId,
            categoryId,
            name = "A Different Laptop", // material change under the same key
        };
        var second = await SendCreateAsync(client, companyId, changedPayload, idempotencyKey);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        Assert.Single(await db.Assets.Where(a => a.CompanyId == companyId).ToListAsync());
    }

    [Fact]
    public async Task Concurrent_Delivery_Of_The_Same_Key_Only_Commits_One_Asset()
    {
        var (companyId, categoryId, client) = await SetupAutomaticNumberingCompanyAsync();
        var idempotencyKey = Guid.NewGuid();
        var payload = CreateAssetPayload(companyId, categoryId);

        // Two independent, concurrently-in-flight deliveries of the exact same scoped key - proves
        // the Postgres unique-violation-then-replay race handling on the real asset-number unique
        // constraint, which EF's InMemory provider cannot exercise.
        var first = SendCreateAsync(client, companyId, payload, idempotencyKey);
        var second = SendCreateAsync(client, companyId, payload, idempotencyKey);
        var responses = await Task.WhenAll(first, second);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<AssetPayload>()));
        Assert.Equal(bodies[0]!.Id, bodies[1]!.Id);
        Assert.Equal(bodies[0]!.AssetNumber, bodies[1]!.AssetNumber);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();

        Assert.Single(await db.Assets.Where(a => a.CompanyId == companyId).ToListAsync());
        Assert.Single(await db.AuditOutboxEntries.Where(e => e.CompanyId == companyId).ToListAsync());
    }

    [Fact]
    public async Task Audit_Delivery_Failure_Is_Recovered_Without_Repeating_The_Mutation()
    {
        var (companyId, categoryId, client) = await SetupAutomaticNumberingCompanyAsync();
        var idempotencyKey = Guid.NewGuid();
        var payload = CreateAssetPayload(companyId, categoryId);

        var response = await SendCreateAsync(client, companyId, payload, idempotencyKey);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using (var failScope = _factory.Services.CreateScope())
        {
            var db = failScope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            await db.DispatchPendingAsync(
                db.AuditOutboxEntries, new FailingAuditEventPublisher(), DateTimeOffset.UtcNow, 200,
                NullLogger.Instance, CancellationToken.None);
        }

        using (var checkScope = _factory.Services.CreateScope())
        {
            var db = checkScope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            var entry = await db.AuditOutboxEntries.SingleAsync(e => e.CompanyId == companyId);
            Assert.Null(entry.DispatchedAt);
            Assert.Equal(1, entry.AttemptCount);
            Assert.NotNull(entry.NextAttemptAt);

            Assert.Single(await db.Assets.Where(a => a.CompanyId == companyId).ToListAsync());
        }

        var publishedCompanyIds = new List<Guid>();
        using (var successScope = _factory.Services.CreateScope())
        {
            var db = successScope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            var succeedingPublisher = new CountingAuditEventPublisher(evt =>
            {
                if (evt is AssetCreatedAuditEvent created)
                    publishedCompanyIds.Add(created.CompanyId);
            });
            await db.DispatchPendingAsync(
                db.AuditOutboxEntries, succeedingPublisher, DateTimeOffset.UtcNow.AddMinutes(10), 200,
                NullLogger.Instance, CancellationToken.None);
        }

        Assert.Single(publishedCompanyIds, id => id == companyId);

        using (var finalScope = _factory.Services.CreateScope())
        {
            var db = finalScope.ServiceProvider.GetRequiredService<AssetsDbContext>();
            var entry = await db.AuditOutboxEntries.SingleAsync(e => e.CompanyId == companyId);
            Assert.NotNull(entry.DispatchedAt);

            // Replay the original request (same key, unchanged payload) - must return the original
            // response and must NOT enqueue a second outbox entry or asset.
            var replay = await SendCreateAsync(client, companyId, payload, idempotencyKey);
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Single(await db.AuditOutboxEntries.Where(e => e.CompanyId == companyId).ToListAsync());
            Assert.Single(await db.Assets.Where(a => a.CompanyId == companyId).ToListAsync());
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

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
        name = "Idempotency Test Laptop",
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

            settings.UpdateAssetNumberSettings(AssetNumberMode.Automatic, "IDEM-", 1, 4, now);
            await db.SaveChangesAsync();
        }

        var categoryResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/asset-categories", new { companyId, name = "Idempotency Category" });
        categoryResponse.EnsureSuccessStatusCode();
        var category = await categoryResponse.Content.ReadFromJsonAsync<IdPayload>();

        return (companyId, category!.Id, client);
    }

    private sealed class FailingAuditEventPublisher : IAuditEventPublisher
    {
        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit publisher failure.");
    }

    private sealed class CountingAuditEventPublisher(Action<object> onPublish) : IAuditEventPublisher
    {
        public Task PublishAsync<TAuditEvent>(TAuditEvent auditEvent, CancellationToken cancellationToken)
        {
            onPublish(auditEvent!);
            return Task.CompletedTask;
        }
    }

    private sealed record IdPayload(Guid Id);

    private sealed record AssetPayload(
        Guid Id, Guid CompanyId, string AssetNumber, Guid CategoryId, string Name,
        string? Manufacturer, string? Model, string? SerialNumber,
        DateOnly? PurchaseDate, decimal? PurchasePrice, string Status,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
