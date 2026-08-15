using System.Net;
using System.Net.Http.Json;

using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// See ExtendCustomerTrialEndpointTests for the shared platform-admin allow-list test pattern
/// this class follows.
/// </summary>
[Collection("Integration")]
public class GetDeletionQueueEndpointTests
{
    private const string AllowListedEmail = "priya.shah@acme.example";

    private readonly ApiWebApplicationFactory _factory;

    public GetDeletionQueueEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(Guid userId, string? email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        if (!string.IsNullOrWhiteSpace(email))
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        }

        return client;
    }

    private async Task<Company> SeedCompanyWithPendingDeletionAsync(string name, DateTimeOffset now)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();

        var company = Company.Create(Guid.NewGuid(), name, now);
        var subscription = CustomerSubscription.StartTrial(company.Id, now, trialLengthDays: 14);
        subscription.ScheduleDeletion(Guid.NewGuid(), now.AddDays(30), now);

        db.Companies.Add(company);
        db.CustomerSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return company;
    }

    private const string Url = "/api/companies/admin/deletion-queue";

    [Fact]
    public async Task Get_DeletionQueue_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_DeletionQueue_Returns_Unauthorized_For_Authenticated_Caller_Not_On_AllowList()
    {
        using var client = ClientFor(Guid.NewGuid(), "not-allow-listed@example.com");

        var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_DeletionQueue_Returns_Ok_With_Only_Companies_That_Have_A_Scheduled_Deletion()
    {
        var now = DateTimeOffset.UtcNow;
        var pendingCompany = await SeedCompanyWithPendingDeletionAsync("Pending Deletion Co", now);

        using var client = ClientFor(Guid.NewGuid(), AllowListedEmail);

        var response = await client.GetAsync(Url);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<DeletionQueuePayload>();
        Assert.NotNull(payload);
        Assert.Contains(
            payload!.Items,
            i => i.CompanyId == pendingCompany.Id
                && i.CompanyName == "Pending Deletion Co"
                && i.CancelledAt == null
                && i.ExecutedAt == null);
    }

    private sealed record DeletionQueuePayload(List<DeletionQueueItemPayload> Items);

    private sealed record DeletionQueueItemPayload(
        Guid CompanyId,
        string CompanyName,
        DateTimeOffset ScheduledAt,
        DateTimeOffset? CancelledAt,
        DateTimeOffset? ExecutedAt);
}
