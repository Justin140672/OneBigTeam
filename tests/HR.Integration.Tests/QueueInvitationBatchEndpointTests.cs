using System.Net;
using System.Net.Http.Json;
using Hangfire.Common;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class QueueInvitationBatchEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaac1-0000-0000-0000-000000000001");

    public QueueInvitationBatchEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUser, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    private HttpClient AuthenticatedClient(Guid companyId, Guid? userId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, (userId ?? AdminUser).ToString());
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
    public async Task Post_QueueInvitationBatch_Returns_Forbidden_For_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee);

        using var client = AuthenticatedClient(companyId, employeeUserId);

        var response = await client.PostAsJsonAsync(Url(companyId), new { companyId, employeeIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_QueueInvitationBatch_Queues_Eligible_Employees_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Bulk", "Invitee");

        var response = await client.PostAsJsonAsync(Url(companyId), new { companyId, employeeIds = new[] { employeeId } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.QueuedCount);
        Assert.Empty(payload.Excluded);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var batch = await db.InvitationBatches.SingleAsync(b => b.Id == payload.BatchId);
        Assert.Equal(companyId, batch.CompanyId);
        var recipients = await db.InvitationBatchRecipients.Where(r => r.BatchId == batch.Id).ToListAsync();
        Assert.Single(recipients);
        Assert.Equal(employeeId, recipients[0].EmployeeId);

        var jobClient = (FakeBackgroundJobClient)scope.ServiceProvider.GetRequiredService<Hangfire.IBackgroundJobClient>();
        Assert.Contains(jobClient.CreatedJobs, j =>
            j.Type == typeof(ProcessInvitationBatchJob) && j.Method.Name == nameof(ProcessInvitationBatchJob.RunAsync));
    }

    [Fact]
    public async Task Post_QueueInvitationBatch_Returns_Validation_Failure_When_No_Eligible_Recipients_Remain()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        // An id that resolves to nothing in this company's candidate list.
        var response = await client.PostAsJsonAsync(Url(companyId), new { companyId, employeeIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await db.InvitationBatches.AnyAsync(b => b.CompanyId == companyId));
    }

    [Fact]
    public async Task Post_QueueInvitationBatch_Repeated_Request_With_Same_IdempotencyKey_Returns_Same_BatchId()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var employeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Idem", "Potent");
        var idempotencyKey = Guid.NewGuid().ToString();

        HttpRequestMessage BuildRequest() => new(HttpMethod.Post, Url(companyId))
        {
            Content = JsonContent.Create(new { companyId, employeeIds = new[] { employeeId } }),
            Headers = { { "Idempotency-Key", idempotencyKey } },
        };

        var firstResponse = await client.SendAsync(BuildRequest());
        var secondResponse = await client.SendAsync(BuildRequest());

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var first = await firstResponse.Content.ReadFromJsonAsync<Payload>();
        var second = await secondResponse.Content.ReadFromJsonAsync<Payload>();
        Assert.Equal(first!.BatchId, second!.BatchId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var batchCount = await db.InvitationBatches.CountAsync(b => b.CompanyId == companyId);
        Assert.Equal(1, batchCount);
    }

    [Fact]
    public async Task Post_QueueInvitationBatch_Excludes_A_Cross_Company_Employee_Id_Rather_Than_Queuing_It()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var crossCompanyEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, otherCompanyId, "Other", "Tenant");

        var response = await client.PostAsJsonAsync(Url(companyId), new { companyId, employeeIds = new[] { crossCompanyEmployeeId } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // zero eligible remain

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await db.InvitationBatchRecipients.AnyAsync(r => r.EmployeeId == crossCompanyEmployeeId));
    }

    private sealed record Payload(Guid BatchId, int QueuedCount, IReadOnlyList<ExcludedPayload> Excluded);

    private sealed record ExcludedPayload(Guid EmployeeId, string? Email, string Reason);
}
