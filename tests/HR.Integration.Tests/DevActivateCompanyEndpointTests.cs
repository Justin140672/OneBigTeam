using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Covers the dev-only /api/dev/activate-company endpoint (Features/DevActivateCompany), which
/// replaces the removed /api/dev/confirm-email stub — local dev/demo bypass to flip a company
/// straight to Active without going through Supabase/VerifyEmail.
/// </summary>
[Collection("Integration")]
public class DevActivateCompanyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public DevActivateCompanyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    private async Task<Guid> CreatePendingCompanyAsync()
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

    [Fact]
    public async Task Post_DevActivateCompany_Activates_The_Company_And_Returns_NoContent_In_Development()
    {
        var companyId = await CreatePendingCompanyAsync();

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/activate-company", new { companyId });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var company = await companiesDb.Companies.SingleAsync(c => c.Id == companyId);
        Assert.Equal(CompanyStatus.Active, company.Status);
        Assert.True(company.IsActive);
    }

    [Fact]
    public async Task Post_DevActivateCompany_Returns_NotFound_Outside_Development()
    {
        // Reuse the shared collection's already-migrated Postgres container rather than spinning
        // up a dedicated one — only the hosting environment needs to differ. WithWebHostBuilder
        // composes on top of ApiWebApplicationFactory's ConfigureWebHost (TestAuthHandler, fakes).
        using var productionFactory = _factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));

        using var client = productionFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/dev/activate-company", new { companyId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record SignUpPayload(Guid UserId, Guid CompanyId, string Email, string FirstName, string LastName);
}
