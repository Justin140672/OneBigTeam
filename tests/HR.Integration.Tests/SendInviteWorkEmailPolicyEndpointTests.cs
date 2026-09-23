using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 9: the legacy POST /api/companies/{companyId}/employees/{employeeId}/invite endpoint
/// (policy employee:manage) applies the same work-email rule as InviteEmployeeUser — a public
/// domain is refused with 400 work_email_required before any existing invite is replaced, any new
/// invite is persisted or any email is sent. See UserInviteEndpointTests for the general contract.
/// </summary>
[Collection("Integration")]
public class SendInviteWorkEmailPolicyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid InviteAdminUser = new("b9e10009-0000-0000-0000-000000000003");

    public SendInviteWorkEmailPolicyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, InviteAdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private async Task<HttpClient> AuthenticatedClientAsync(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, InviteAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, InviteAdminUser, SystemRoles.HrAdministrator, companyId);
        return client;
    }

    private static string Url(Guid companyId, Guid employeeId) =>
        $"/api/companies/{companyId}/employees/{employeeId}/invite";

    [Fact]
    public async Task Post_Invite_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            Url(companyId, employeeId), new { companyId, employeeId, email = "new.hire@gmail.com" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("Outlook.Com")]
    [InlineData("10minutemail.com")]
    public async Task Post_Invite_Returns_400_WorkEmailRequired_For_Public_Domain_And_Persists_Nothing(string domain)
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AuthenticatedClientAsync(companyId);
        var email = $"legacy.hire.{Guid.NewGuid():N}@{domain}";

        var response = await client.PostAsJsonAsync(Url(companyId, employeeId), new { companyId, employeeId, email });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("work_email_required", doc.RootElement.GetProperty("code").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await db.UserInvites.AnyAsync(i => i.EmployeeId == employeeId));
        Assert.DoesNotContain(_factory.EmailSender.Sent, e => string.Equals(e.ToEmail, email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Post_Invite_Public_Domain_Rejection_Does_Not_Replace_An_Existing_Pending_Invite()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AuthenticatedClientAsync(companyId);
        var existingInviteId = await IdentityUserAdminTestHelpers.SeedInviteAsync(
            _factory, companyId, employeeId, $"existing.{Guid.NewGuid():N}@acme.example");

        var response = await client.PostAsJsonAsync(
            Url(companyId, employeeId), new { companyId, employeeId, email = $"replacement.{Guid.NewGuid():N}@gmail.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var invites = await db.UserInvites.AsNoTracking().Where(i => i.EmployeeId == employeeId).ToListAsync();
        var remaining = Assert.Single(invites);
        Assert.Equal(existingInviteId, remaining.Id);
        Assert.Null(remaining.CancelledAt);
    }

    [Fact]
    public async Task Post_Invite_Succeeds_For_Organisation_Domain()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using var client = await AuthenticatedClientAsync(companyId);
        var email = $"legacy.hire.{Guid.NewGuid():N}@brightsparks-consulting.co.uk";

        var response = await client.PostAsJsonAsync(Url(companyId, employeeId), new { companyId, employeeId, email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(await db.UserInvites.AnyAsync(i => i.EmployeeId == employeeId && i.Email == email));
        Assert.Contains(_factory.EmailSender.Sent, e => e.ToEmail == email);
    }
}
