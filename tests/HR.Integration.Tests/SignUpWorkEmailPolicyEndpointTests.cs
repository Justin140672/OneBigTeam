using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Companies.Persistence;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 9: POST /api/signup must refuse public/disposable email domains for the new admin account
/// with a 400 carrying the machine-readable <c>work_email_required</c> code, and must not create a
/// company, profile, Supabase user or anything else for a rejected address.
///
/// Note: /api/signup is an anonymous endpoint (self-service registration), so there is no 401
/// case to cover here — see SignUpEndpointTests.Post_SignUp_Does_Not_Return_Unauthorized_For_Anonymous_Request.
/// </summary>
[Collection("Integration")]
public class SignUpWorkEmailPolicyEndpointTests
{
    private const string WorkEmailRequiredMessage =
        "Please use your organisation's work email address. Public email services such as Gmail, Hotmail and Outlook.com cannot be used to create an account.";

    private const string RejectedEventType = "account-creation.email-domain-rejected";

    private readonly ApiWebApplicationFactory _factory;

    public SignUpWorkEmailPolicyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _factory.SupabaseAuthGateway.Reset();
    }

    private static object SignUpRequest(string companyName, string email) => new
    {
        companyName,
        adminFirstName = "Ada",
        adminLastName = "Lovelace",
        adminEmail = email,
        password = "P@ssw0rd123",
    };

    private async Task<int> CountPublicSignupRejectionAuditsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        return await auditDb.AuditEvents.CountAsync(e => e.EventType == RejectedEventType && e.CompanyId == Guid.Empty);
    }

    private async Task AssertNothingCreatedAsync(string companyName, string email)
    {
        using var scope = _factory.Services.CreateScope();

        var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        Assert.False(await companiesDb.Companies.AnyAsync(c => c.Name == companyName));

        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var lowered = email.Trim().ToLowerInvariant();
        Assert.False(await identityDb.UserProfiles.AnyAsync(p => p.Email.ToLower() == lowered));

        Assert.DoesNotContain(_factory.SupabaseAuthGateway.CreatedUsers, u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_factory.SupabaseAuthGateway.ConfirmedUsersCreated, u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_factory.SupabaseAuthGateway.PendingUsersCreatedWithMetadata, u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(_factory.SupabaseAuthGateway.ResentEmails, u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("GMAIL.COM")]
    public async Task Post_SignUp_Returns_400_WorkEmailRequired_For_Gmail_And_Creates_Nothing(string domain)
    {
        using var client = _factory.CreateClient();
        var companyName = $"Gmail-Co-{Guid.NewGuid():N}";
        var email = $"ada-{Guid.NewGuid():N}@{domain}";
        var auditsBefore = await CountPublicSignupRejectionAuditsAsync();

        var response = await client.PostAsJsonAsync("/api/signup", SignUpRequest(companyName, email));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("work_email_required", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(WorkEmailRequiredMessage, doc.RootElement.GetProperty("error").GetString());

        await AssertNothingCreatedAsync(companyName, email);

        Assert.Equal(auditsBefore + 1, await CountPublicSignupRejectionAuditsAsync());
        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var latest = await auditDb.AuditEvents
            .Where(e => e.EventType == RejectedEventType && e.CompanyId == Guid.Empty)
            .OrderByDescending(e => e.OccurredAt)
            .FirstAsync();
        Assert.Equal(AuditActorType.Anonymous, latest.ActorType);
        Assert.Contains("gmail.com", latest.MetadataJson);
        Assert.DoesNotContain("@", latest.MetadataJson ?? string.Empty);
        Assert.DoesNotContain("@", latest.Summary ?? string.Empty);
    }

    [Theory]
    [InlineData("hotmail.com")]
    [InlineData("hotmail.co.uk")]
    [InlineData("outlook.com")]
    [InlineData("yahoo.com")]
    [InlineData("mailinator.com")]
    [InlineData("mx.guerrillamail.com")]
    public async Task Post_SignUp_Returns_400_WorkEmailRequired_For_Other_Public_Or_Disposable_Domains(string domain)
    {
        using var client = _factory.CreateClient();
        var companyName = $"Public-Co-{Guid.NewGuid():N}";
        var email = $"ada-{Guid.NewGuid():N}@{domain}";

        var response = await client.PostAsJsonAsync("/api/signup", SignUpRequest(companyName, email));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("work_email_required", doc.RootElement.GetProperty("code").GetString());

        await AssertNothingCreatedAsync(companyName, email);
    }

    [Theory]
    [InlineData("brightsparks-consulting.co.uk")]
    [InlineData("olive.com")]                     // must not be caught by "live.com"
    public async Task Post_SignUp_Succeeds_For_Organisation_Domain(string domain)
    {
        using var client = _factory.CreateClient();
        var companyName = $"Org-Co-{Guid.NewGuid():N}";
        var email = $"ada-{Guid.NewGuid():N}@{domain}";

        var response = await client.PostAsJsonAsync("/api/signup", SignUpRequest(companyName, email));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_factory.SupabaseAuthGateway.CreatedUsers, u => u.Email == email);

        using var scope = _factory.Services.CreateScope();
        var companiesDb = scope.ServiceProvider.GetRequiredService<CompaniesDbContext>();
        Assert.True(await companiesDb.Companies.AnyAsync(c => c.Name == companyName));
        var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(await identityDb.UserProfiles.AnyAsync(p => p.Email == email));
    }

    [Fact]
    public async Task Post_SignUp_Existing_Account_Conflict_Is_Still_409_For_An_Organisation_Domain()
    {
        using var client = _factory.CreateClient();
        var email = $"ada-{Guid.NewGuid():N}@acme.example";

        var first = await client.PostAsJsonAsync("/api/signup", SignUpRequest($"Org-Co-{Guid.NewGuid():N}", email));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/signup", SignUpRequest($"Org-Co-{Guid.NewGuid():N}", email));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var doc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("conflict", doc.RootElement.GetProperty("code").GetString());
    }
}
