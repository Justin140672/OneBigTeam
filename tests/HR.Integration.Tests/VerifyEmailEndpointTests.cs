using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Covers Phase D's real email-verification endpoint (Features/VerifyEmail). Confirmed via live
/// testing against a real Supabase project that Supabase uses the implicit/fragment redirect flow
/// here, not PKCE — the caller (HR.Web's /verify-email-complete) already holds a genuine Supabase
/// access token by the time it calls this endpoint, presented as a normal Authorization: Bearer
/// header. TestAuthHandler's X-Test-User header stands in for that Bearer token here (its value
/// becomes the "sub" claim, matching a real Supabase JWT's shape) — exercises the full
/// SignUp -> VerifyEmail flow against the stubbed ISupabaseAuthGateway
/// (ApiWebApplicationFactory.SupabaseAuthGateway) for the SignUp half only; VerifyEmail itself no
/// longer calls Supabase at all.
/// </summary>
[Collection("Integration")]
public class VerifyEmailEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public VerifyEmailEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    private static object ValidSignUpRequest(string email) => new
    {
        companyName = $"Acme-{Guid.NewGuid():N}",
        adminFirstName = "Ada",
        adminLastName = "Lovelace",
        adminEmail = email,
        password = "P@ssw0rd123",
    };

    private async Task<(Guid CompanyId, Guid SupabaseUserId)> SignUpPendingCompanyAsync()
    {
        using var client = _factory.CreateClient();
        var email = $"ada-{Guid.NewGuid():N}@example.com";
        var supabaseUserId = Guid.NewGuid();
        _factory.SupabaseAuthGateway.UserIdToReturn = supabaseUserId;

        var response = await client.PostAsJsonAsync("/api/signup", ValidSignUpRequest(email));
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<SignUpPayload>();
        return (payload!.CompanyId, supabaseUserId);
    }

    private HttpClient VerifiedCallerClient(Guid supabaseUserId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, supabaseUserId.ToString());
        return client;
    }

    [Fact]
    public async Task Post_VerifyEmail_Activates_The_Company_On_Happy_Path()
    {
        var (companyId, supabaseUserId) = await SignUpPendingCompanyAsync();

        using var client = VerifiedCallerClient(supabaseUserId);
        var response = await client.PostAsync("/api/verify-email", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<VerifyEmailPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);
        Assert.NotEqual(Guid.Empty, payload.UserId);

        using var scope = _factory.Services.CreateScope();
        var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var company = await companiesDb.Companies.SingleAsync(c => c.Id == companyId);
        Assert.Equal(CompanyStatus.Active, company.Status);
        Assert.True(company.IsActive);
    }

    [Fact]
    public async Task Post_VerifyEmail_Repeat_Click_Stays_Active_And_Still_Returns_Success()
    {
        var (companyId, supabaseUserId) = await SignUpPendingCompanyAsync();

        using var client = VerifiedCallerClient(supabaseUserId);

        var first = await client.PostAsync("/api/verify-email", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Simulates a double-click / reopened tab: same still-valid caller, second call against a
        // company that's already Active.
        var second = await client.PostAsync("/api/verify-email", content: null);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var payload = await second.Content.ReadFromJsonAsync<VerifyEmailPayload>();
        Assert.NotNull(payload);
        Assert.Equal(companyId, payload!.CompanyId);

        using var scope = _factory.Services.CreateScope();
        var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        var company = await companiesDb.Companies.SingleAsync(c => c.Id == companyId);
        Assert.Equal(CompanyStatus.Active, company.Status);
    }

    [Fact]
    public async Task Post_VerifyEmail_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/verify-email", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_VerifyEmail_Returns_Forbidden_For_Caller_With_No_Matching_UserProfile()
    {
        // A caller id that was never seeded via SignUp (no UserProfile, no roles) — the closest
        // integration-test equivalent to "a valid-looking token for someone who was never really
        // signed up", which the middleware/role check rejects the same way it would a stale/
        // invalid one, without needing a genuinely-expired real Supabase token.
        using var client = VerifiedCallerClient(Guid.NewGuid());

        var response = await client.PostAsync("/api/verify-email", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private sealed record SignUpPayload(Guid UserId, Guid CompanyId, string Email, string FirstName, string LastName);

    private sealed record VerifyEmailPayload(Guid UserId, Guid CompanyId);
}
