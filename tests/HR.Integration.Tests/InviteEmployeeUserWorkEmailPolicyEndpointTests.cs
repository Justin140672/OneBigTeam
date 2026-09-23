using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Ticket 9: POST /api/companies/{companyId}/employees/{employeeId}/invite-user must refuse a
/// public/disposable invited address (400 work_email_required) before any invite row is persisted
/// or any invitation email is sent. See InviteEmployeeUserEndpointTests for the general contract.
/// </summary>
[Collection("Integration")]
public class InviteEmployeeUserWorkEmailPolicyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("b9e10009-0000-0000-0000-000000000001");

    public InviteEmployeeUserWorkEmailPolicyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private static string Url(Guid companyId, Guid employeeId) =>
        $"/api/companies/{companyId}/employees/{employeeId}/invite-user";

    [Fact]
    public async Task Post_InviteEmployeeUser_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            Url(companyId, employeeId),
            new { companyId, employeeId, email = "new.hire@gmail.com", roleIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("HOTMAIL.CO.UK")]
    [InlineData("yopmail.com")]
    public async Task Post_InviteEmployeeUser_Returns_400_WorkEmailRequired_For_Public_Domain_And_Persists_Nothing(string domain)
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Public", "Invitee");
        var roleId = await IdentityUserAdminTestHelpers.SeedRoleAsync(_factory, $"Role-{Guid.NewGuid():N}");
        var email = $"new.hire.{Guid.NewGuid():N}@{domain}";

        var response = await client.PostAsJsonAsync(
            Url(companyId, employeeId),
            new { companyId, employeeId, email, roleIds = new[] { roleId } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("work_email_required", doc.RootElement.GetProperty("code").GetString());
        Assert.StartsWith("Please use your organisation's work email address.", doc.RootElement.GetProperty("error").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await db.UserInvites.AnyAsync(i => i.EmployeeId == employeeId));

        Assert.DoesNotContain(_factory.EmailSender.Sent, e => string.Equals(e.ToEmail, email, StringComparison.OrdinalIgnoreCase));

        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var rejection = await auditDb.AuditEvents.SingleAsync(e =>
            e.EventType == "account-creation.email-domain-rejected" && e.EmployeeId == employeeId);
        Assert.Equal(companyId, rejection.CompanyId);
        Assert.Equal(AuditActorType.Human, rejection.ActorType);
        Assert.DoesNotContain("@", rejection.MetadataJson ?? string.Empty);
    }

    [Fact]
    public async Task Post_InviteEmployeeUser_Succeeds_For_Organisation_Domain()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Org", "Invitee");
        var roleId = await IdentityUserAdminTestHelpers.SeedRoleAsync(_factory, $"Role-{Guid.NewGuid():N}");
        var email = $"new.hire.{Guid.NewGuid():N}@brightsparks-consulting.co.uk";

        var response = await client.PostAsJsonAsync(
            Url(companyId, employeeId),
            new { companyId, employeeId, email, roleIds = new[] { roleId } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var invite = await db.UserInvites.SingleAsync(i => i.EmployeeId == employeeId);
        Assert.Equal(email, invite.Email);
    }
}
