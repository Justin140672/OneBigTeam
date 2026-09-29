using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class DevExpireCompanyTrialEndpointTests
{
    private const string E2eVariable = "E2E_TESTING";

    private readonly ApiWebApplicationFactory _factory;

    public DevExpireCompanyTrialEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    private async Task<Guid> SignUpCompanyAsync()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/signup", new
        {
            companyName = $"Acme-{Guid.NewGuid():N}",
            adminFirstName = "Ada",
            adminLastName = "Lovelace",
            adminEmail = $"ada-{Guid.NewGuid():N}@example.com",
            password = "P@ssw0rd123",
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<SignUpPayload>();
        return payload!.CompanyId;
    }

    private static async Task<T> WithE2E<T>(string? value, Func<Task<T>> action)
    {
        var original = Environment.GetEnvironmentVariable(E2eVariable);
        Environment.SetEnvironmentVariable(E2eVariable, value);
        try
        {
            return await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(E2eVariable, original);
        }
    }

    [Fact]
    public async Task Post_ExpireCompanyTrial_Makes_Company_ReadOnly_When_E2E_Testing_Is_On()
    {
        var companyId = await SignUpCompanyAsync();

        var status = await WithE2E("true", async () =>
        {
            using var client = _factory.CreateClient();
            return (await client.PostAsJsonAsync("/api/dev/expire-company-trial", new { companyId })).StatusCode;
        });

        Assert.Equal(HttpStatusCode.NoContent, status);

        using var scope = _factory.Services.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<ISubscriptionStatusReader>();
        var snapshot = await reader.GetStatusAsync(companyId, CancellationToken.None);
        Assert.True(snapshot.IsReadOnly);
        Assert.Equal(SubscriptionStatus.TrialExpired, snapshot.Status);
    }

    [Fact]
    public async Task Post_ExpireCompanyTrial_Returns_NotFound_When_E2E_Testing_Is_Off_And_Mutates_Nothing()
    {
        var companyId = await SignUpCompanyAsync();

        var status = await WithE2E(null, async () =>
        {
            using var client = _factory.CreateClient();
            return (await client.PostAsJsonAsync("/api/dev/expire-company-trial", new { companyId })).StatusCode;
        });

        Assert.Equal(HttpStatusCode.NotFound, status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var subscription = await db.CustomerSubscriptions.AsNoTracking().SingleAsync(s => s.CompanyId == companyId);
        Assert.True(subscription.TrialExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Post_ExpireCompanyTrial_Returns_NotFound_For_Unknown_Company()
    {
        var status = await WithE2E("true", async () =>
        {
            using var client = _factory.CreateClient();
            return (await client.PostAsJsonAsync("/api/dev/expire-company-trial", new { companyId = Guid.NewGuid() })).StatusCode;
        });

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    private sealed record SignUpPayload(Guid UserId, Guid CompanyId, string Email, string FirstName, string LastName);
}
