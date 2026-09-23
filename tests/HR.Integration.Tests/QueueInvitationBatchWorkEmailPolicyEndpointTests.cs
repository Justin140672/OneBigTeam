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
/// Ticket 9: POST /api/companies/{companyId}/invitation-batches excludes employees whose work email
/// is on a public/disposable domain (reason "PublicEmailDomain", never queued), still queues the
/// rest, and fails with 400 work_email_required when nobody eligible remains because of the policy.
/// See QueueInvitationBatchEndpointTests for the general contract.
/// </summary>
[Collection("Integration")]
public class QueueInvitationBatchWorkEmailPolicyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("b9e10009-0000-0000-0000-000000000002");

    public QueueInvitationBatchWorkEmailPolicyEndpointTests(ApiWebApplicationFactory factory)
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

    private static string Url(Guid companyId) => $"/api/companies/{companyId}/invitation-batches";

    [Fact]
    public async Task Post_QueueInvitationBatch_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(Url(companyId), new { companyId, employeeIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_QueueInvitationBatch_Mixed_Batch_Queues_Org_Email_And_Excludes_Public_Email()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var orgEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(
            _factory, companyId, "Org", "Worker", workEmail: $"org.worker.{Guid.NewGuid():N}@acme.example");
        var publicEmail = $"public.worker.{Guid.NewGuid():N}@gmail.com";
        var publicEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(
            _factory, companyId, "Public", "Worker", workEmail: publicEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId), new { companyId, employeeIds = new[] { orgEmployeeId, publicEmployeeId } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.QueuedCount);
        var excluded = Assert.Single(payload.Excluded);
        Assert.Equal(publicEmployeeId, excluded.EmployeeId);
        Assert.Equal("PublicEmailDomain", excluded.Reason);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var recipients = await db.InvitationBatchRecipients.Where(r => r.BatchId == payload.BatchId).ToListAsync();
        var recipient = Assert.Single(recipients);
        Assert.Equal(orgEmployeeId, recipient.EmployeeId);
        Assert.False(await db.InvitationBatchRecipients.AnyAsync(r => r.EmployeeId == publicEmployeeId));
        Assert.False(await db.UserInvites.AnyAsync(i => i.EmployeeId == publicEmployeeId));
    }

    [Fact]
    public async Task Post_QueueInvitationBatch_All_Public_Batch_Returns_400_WorkEmailRequired_And_Creates_No_Batch()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var gmailEmail = $"all.public.{Guid.NewGuid():N}@gmail.com";
        var hotmailEmail = $"all.public.{Guid.NewGuid():N}@hotmail.com";
        var gmailEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(
            _factory, companyId, "Gmail", "Worker", workEmail: gmailEmail);
        var hotmailEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(
            _factory, companyId, "Hotmail", "Worker", workEmail: hotmailEmail);

        var response = await client.PostAsJsonAsync(
            Url(companyId), new { companyId, employeeIds = new[] { gmailEmployeeId, hotmailEmployeeId } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("work_email_required", doc.RootElement.GetProperty("code").GetString());
        var message = doc.RootElement.GetProperty("error").GetString();
        Assert.Contains(gmailEmail, message);
        Assert.Contains(hotmailEmail, message);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await db.InvitationBatches.AnyAsync(b => b.CompanyId == companyId));
    }

    private sealed record Payload(Guid BatchId, int QueuedCount, IReadOnlyList<ExcludedPayload> Excluded);

    private sealed record ExcludedPayload(Guid EmployeeId, string? Email, string Reason);
}
