using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class RetryInvitationBatchEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUser = new("aaaaaaf1-0000-0000-0000-000000000001");

    public RetryInvitationBatchEndpointTests(ApiWebApplicationFactory factory)
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

    private async Task<(Guid BatchId, Guid FailedRecipientId, Guid SentRecipientId)> SeedBatchWithFailedRecipientAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;

        var batch = InvitationBatch.Create(companyId, Guid.NewGuid(), now, null);
        batch.MarkProcessing(now);
        batch.MarkCompleted(now.AddMinutes(1));
        db.InvitationBatches.Add(batch);

        var sent = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), "sent@test.com", now);
        sent.MarkProcessing();
        sent.MarkSent(now.AddMinutes(1));

        var failed = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), "failed@test.com", now);
        failed.MarkProcessing();
        failed.MarkFailed("Email delivery failed");

        db.InvitationBatchRecipients.AddRange(sent, failed);
        await db.SaveChangesAsync();

        return (batch.Id, failed.Id, sent.Id);
    }

    [Fact]
    public async Task Post_RetryInvitationBatch_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = Guid.NewGuid();

        var response = await client.PostAsync(
            $"/api/companies/{companyId}/invitation-batches/{Guid.NewGuid()}/retry", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_RetryInvitationBatch_Resets_Only_Failed_Recipients_On_Happy_Path()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (batchId, failedRecipientId, sentRecipientId) = await SeedBatchWithFailedRecipientAsync(companyId);

        var response = await client.PostAsync($"/api/companies/{companyId}/invitation-batches/{batchId}/retry", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.RetryCount);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var failedRecipient = await db.InvitationBatchRecipients.SingleAsync(r => r.Id == failedRecipientId);
        var sentRecipient = await db.InvitationBatchRecipients.SingleAsync(r => r.Id == sentRecipientId);

        Assert.Equal(InvitationBatchRecipient.StatusWaiting, failedRecipient.Status);
        Assert.Equal(InvitationBatchRecipient.StatusSent, sentRecipient.Status); // untouched
    }

    [Fact]
    public async Task Post_RetryInvitationBatch_Returns_Validation_Failure_When_Nothing_To_Retry()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;
        var batch = InvitationBatch.Create(companyId, Guid.NewGuid(), now, null);
        var sent = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), "sent2@test.com", now);
        sent.MarkProcessing();
        sent.MarkSent(now);
        db.InvitationBatches.Add(batch);
        db.InvitationBatchRecipients.Add(sent);
        await db.SaveChangesAsync();

        var response = await client.PostAsync($"/api/companies/{companyId}/invitation-batches/{batch.Id}/retry", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_RetryInvitationBatch_Returns_NotFound_For_Batch_Belonging_To_Different_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);
        var (batchId, _, _) = await SeedBatchWithFailedRecipientAsync(otherCompanyId);

        var response = await client.PostAsync($"/api/companies/{companyId}/invitation-batches/{batchId}/retry", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Exercises a real failing send through the real <see cref="ProcessInvitationBatchJob"/>
    /// (rather than seeding a StatusFailed recipient directly via MarkFailed, as
    /// <see cref="SeedBatchWithFailedRecipientAsync"/> does), by queuing a real batch via the
    /// QueueInvitationBatch endpoint and then running the job exactly as
    /// FakeBackgroundJobClient's recorded job args describe — Hangfire itself never executes jobs
    /// in this test harness, so the job must be invoked directly.
    /// </summary>
    [Fact]
    public async Task ProcessInvitationBatchJob_Marks_Only_The_Configured_Recipient_Failed_When_Its_Send_Fails()
    {
        var companyId = Guid.NewGuid();
        using var client = AuthenticatedClient(companyId);

        var succeedingEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Will", "Succeed");
        var failingEmployeeId = await IdentityUserAdminTestHelpers.SeedEmployeeAsync(_factory, companyId, "Will", "Fail");

        string succeedingEmail;
        string failingEmail;
        using (var seedScope = _factory.Services.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
            succeedingEmail = await seedDb.Employees.Where(e => e.Id == succeedingEmployeeId).Select(e => e.WorkEmail).SingleAsync();
            failingEmail = await seedDb.Employees.Where(e => e.Id == failingEmployeeId).Select(e => e.WorkEmail).SingleAsync();
        }

        _factory.InvitationEmailSender.FailFor(failingEmail);
        try
        {
            var queueResponse = await client.PostAsJsonAsync(
                $"/api/companies/{companyId}/invitation-batches",
                new { companyId, employeeIds = new[] { succeedingEmployeeId, failingEmployeeId } });
            Assert.Equal(HttpStatusCode.OK, queueResponse.StatusCode);
            var queuePayload = await queueResponse.Content.ReadFromJsonAsync<QueuePayload>();
            Assert.NotNull(queuePayload);
            Assert.Equal(2, queuePayload!.QueuedCount);

            using var jobScope = _factory.Services.CreateScope();
            var job = jobScope.ServiceProvider.GetRequiredService<ProcessInvitationBatchJob>();
            await job.RunAsync(queuePayload.BatchId, CancellationToken.None);

            using var assertScope = _factory.Services.CreateScope();
            var db = assertScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var recipients = await db.InvitationBatchRecipients
                .Where(r => r.BatchId == queuePayload.BatchId)
                .ToListAsync();

            var succeedingRecipient = recipients.Single(r => r.EmployeeId == succeedingEmployeeId);
            var failingRecipient = recipients.Single(r => r.EmployeeId == failingEmployeeId);

            Assert.Equal(InvitationBatchRecipient.StatusSent, succeedingRecipient.Status);
            Assert.Equal(InvitationBatchRecipient.StatusFailed, failingRecipient.Status);
        }
        finally
        {
            // The fake is a singleton shared across the whole "Integration" collection — always
            // clear any configured failures so they can never leak into a later test.
            _factory.InvitationEmailSender.Reset();
        }
    }

    private sealed record QueuePayload(Guid BatchId, int QueuedCount);

    private sealed record Payload(Guid BatchId, int RetryCount);
}
