using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Identity.Tests.Jobs;

[Collection("IdentityDatabase")]
public class ProcessInvitationBatchJobTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private ProcessInvitationBatchJob BuildJob(
        IdentityDbContext db,
        FakeEmployeeInviteCandidateReader candidateReader,
        FakeAuditEventPublisher auditPublisher,
        IInvitationEmailSender? emailSender = null,
        FakeEmployeeNameReader? nameReader = null) =>
        new(
            db,
            Clock,
            nameReader ?? new FakeEmployeeNameReader(),
            candidateReader,
            emailSender ?? new FakeInvitationEmailSender(),
            new FakeInviteLinkBuilder(),
            auditPublisher,
            NullLogger<ProcessInvitationBatchJob>.Instance);

    private async Task<(InvitationBatch Batch, InvitationBatchRecipient Recipient)> SeedWaitingRecipientAsync(
        Guid companyId, string email)
    {
        await using var db = fixture.BuildContext();
        var batch = InvitationBatch.Create(companyId, Guid.NewGuid(), Now, null);
        var recipient = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), email, Now);
        db.InvitationBatches.Add(batch);
        db.InvitationBatchRecipients.Add(recipient);
        await db.SaveChangesAsync();
        return (batch, recipient);
    }

    [Fact]
    public async Task RunAsync_Success_Marks_Sent_And_Sets_EmailSentAt_On_The_Created_Invite()
    {
        var companyId = Guid.NewGuid();
        var (batch, recipient) = await SeedWaitingRecipientAsync(companyId, "sent-success@test.com");
        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(recipient.EmployeeId, "Test Person", recipient.Email, null, null));
        var auditPublisher = new FakeAuditEventPublisher();

        await using var db = fixture.BuildContext();
        await BuildJob(db, candidateReader, auditPublisher).RunAsync(batch.Id, CancellationToken.None);

        await using var verifyDb = fixture.BuildContext();
        var reloadedRecipient = await verifyDb.InvitationBatchRecipients.SingleAsync(r => r.Id == recipient.Id);
        Assert.Equal(InvitationBatchRecipient.StatusSent, reloadedRecipient.Status);
        Assert.NotNull(reloadedRecipient.InviteId);

        var invite = await verifyDb.UserInvites.SingleAsync(i => i.Id == reloadedRecipient.InviteId);
        Assert.NotNull(invite.EmailSentAt);

        var reloadedBatch = await verifyDb.InvitationBatches.SingleAsync(b => b.Id == batch.Id);
        Assert.Equal(InvitationBatch.StatusCompleted, reloadedBatch.Status);

        Assert.Single(auditPublisher.PublishedEvents, e => e is UserInvitedAuditEvent);
    }

    [Fact]
    public async Task RunAsync_Skips_Recipient_Who_Has_Become_Ineligible_Since_Queuing()
    {
        var companyId = Guid.NewGuid();
        var (batch, recipient) = await SeedWaitingRecipientAsync(companyId, "now-ineligible@test.com");
        var candidateReader = new FakeEmployeeInviteCandidateReader(); // no longer eligible
        var auditPublisher = new FakeAuditEventPublisher();

        await using var db = fixture.BuildContext();
        await BuildJob(db, candidateReader, auditPublisher).RunAsync(batch.Id, CancellationToken.None);

        await using var verifyDb = fixture.BuildContext();
        var reloadedRecipient = await verifyDb.InvitationBatchRecipients.SingleAsync(r => r.Id == recipient.Id);
        Assert.Equal(InvitationBatchRecipient.StatusSkipped, reloadedRecipient.Status);
        Assert.Equal("NotEligible", reloadedRecipient.FailureReason);
        Assert.Null(reloadedRecipient.InviteId);
    }

    [Fact]
    public async Task RunAsync_Skips_Recipient_Who_Already_Has_A_Linked_Account()
    {
        var companyId = Guid.NewGuid();
        var (batch, recipient) = await SeedWaitingRecipientAsync(companyId, "already-linked@test.com");

        await using (var seedDb = fixture.BuildContext())
        {
            seedDb.Users.Add(ApplicationUser.Create(recipient.EmployeeId, recipient.Email, "hash", "Test", "User", Now));
            await seedDb.SaveChangesAsync();
        }

        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(recipient.EmployeeId, "Test Person", recipient.Email, null, null));
        var auditPublisher = new FakeAuditEventPublisher();

        await using var db = fixture.BuildContext();
        await BuildJob(db, candidateReader, auditPublisher).RunAsync(batch.Id, CancellationToken.None);

        await using var verifyDb = fixture.BuildContext();
        var reloadedRecipient = await verifyDb.InvitationBatchRecipients.SingleAsync(r => r.Id == recipient.Id);
        Assert.Equal(InvitationBatchRecipient.StatusSkipped, reloadedRecipient.Status);
        Assert.Equal("AlreadyHasAccount", reloadedRecipient.FailureReason);
    }

    [Fact]
    public async Task RunAsync_Email_Failure_Marks_Failed_And_A_Subsequent_Run_Reuses_The_Same_Invite_Instead_Of_Creating_A_Second_One()
    {
        var companyId = Guid.NewGuid();
        var (batch, recipient) = await SeedWaitingRecipientAsync(companyId, "retry-me@test.com");
        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(recipient.EmployeeId, "Retry Person", recipient.Email, null, null));

        // First run: invite gets created, but the email send itself fails.
        await using (var db = fixture.BuildContext())
        {
            var failingSender = new FakeInvitationEmailSender(succeeds: false);
            await BuildJob(db, candidateReader, new FakeAuditEventPublisher(), failingSender).RunAsync(batch.Id, CancellationToken.None);
        }

        await using (var verifyDb = fixture.BuildContext())
        {
            var reloaded = await verifyDb.InvitationBatchRecipients.SingleAsync(r => r.Id == recipient.Id);
            Assert.Equal(InvitationBatchRecipient.StatusFailed, reloaded.Status);
            Assert.NotNull(reloaded.InviteId); // invite WAS created before the send failed

            var inviteCount = await verifyDb.UserInvites.CountAsync(i => i.EmployeeId == recipient.EmployeeId);
            Assert.Equal(1, inviteCount);
        }

        // Simulate RetryInvitationBatch resetting the Failed recipient back to Waiting.
        await using (var resetDb = fixture.BuildContext())
        {
            var toReset = await resetDb.InvitationBatchRecipients.SingleAsync(r => r.Id == recipient.Id);
            toReset.ResetForRetry();
            await resetDb.SaveChangesAsync();
        }

        // Second run: succeeds, must reuse the existing invite rather than creating a second one.
        await using (var db = fixture.BuildContext())
        {
            var succeedingSender = new FakeInvitationEmailSender(succeeds: true);
            await BuildJob(db, candidateReader, new FakeAuditEventPublisher(), succeedingSender).RunAsync(batch.Id, CancellationToken.None);
        }

        await using var finalDb = fixture.BuildContext();
        var finalRecipient = await finalDb.InvitationBatchRecipients.SingleAsync(r => r.Id == recipient.Id);
        Assert.Equal(InvitationBatchRecipient.StatusSent, finalRecipient.Status);

        var finalInviteCount = await finalDb.UserInvites.CountAsync(i => i.EmployeeId == recipient.EmployeeId);
        Assert.Equal(1, finalInviteCount); // still only one UserInvite row for this employee
    }

    [Fact]
    public async Task RunAsync_One_Recipients_Unexpected_Exception_Does_Not_Stop_Others_In_The_Same_Run()
    {
        var companyId = Guid.NewGuid();
        Guid batchId, throwingRecipientId, healthyRecipientId, throwingEmployeeId, healthyEmployeeId;
        const string throwingEmail = "throws@test.com";
        const string healthyEmail = "healthy@test.com";

        await using (var db = fixture.BuildContext())
        {
            var batch = InvitationBatch.Create(companyId, Guid.NewGuid(), Now, null);
            batchId = batch.Id;

            var throwingRecipient = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), throwingEmail, Now);
            var healthyRecipient = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), healthyEmail, Now);
            throwingRecipientId = throwingRecipient.Id;
            healthyRecipientId = healthyRecipient.Id;
            throwingEmployeeId = throwingRecipient.EmployeeId;
            healthyEmployeeId = healthyRecipient.EmployeeId;

            db.InvitationBatches.Add(batch);
            db.InvitationBatchRecipients.AddRange(throwingRecipient, healthyRecipient);
            await db.SaveChangesAsync();
        }

        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(throwingEmployeeId, "Throws Person", throwingEmail, null, null),
            new EmployeeInviteCandidate(healthyEmployeeId, "Healthy Person", healthyEmail, null, null));
        var auditPublisher = new FakeAuditEventPublisher();
        var throwingSender = new ThrowingForEmailInvitationEmailSender(throwingEmail);

        await using var db2 = fixture.BuildContext();
        await BuildJob(db2, candidateReader, auditPublisher, throwingSender).RunAsync(batchId, CancellationToken.None);

        await using var verifyDb = fixture.BuildContext();
        var reloadedThrowing = await verifyDb.InvitationBatchRecipients.SingleAsync(r => r.Id == throwingRecipientId);
        var reloadedHealthy = await verifyDb.InvitationBatchRecipients.SingleAsync(r => r.Id == healthyRecipientId);

        Assert.Equal(InvitationBatchRecipient.StatusFailed, reloadedThrowing.Status);
        Assert.Equal(InvitationBatchRecipient.StatusSent, reloadedHealthy.Status);

        var reloadedBatch = await verifyDb.InvitationBatches.SingleAsync(b => b.Id == batchId);
        Assert.Equal(InvitationBatch.StatusCompleted, reloadedBatch.Status);
    }

    /// <summary>Throws for a specific recipient email's send, to prove one recipient's unexpected
    /// exception (as opposed to a mere "email send returned false") doesn't abort the job's loop.</summary>
    private sealed class ThrowingForEmailInvitationEmailSender(string throwingEmail) : IInvitationEmailSender
    {
        public Task<bool> SendAsync(string toEmail, string? recipientName, string actionUrl, CancellationToken ct = default)
        {
            if (toEmail == throwingEmail)
                throw new InvalidOperationException("Simulated unexpected failure sending this recipient's email.");

            return Task.FromResult(true);
        }
    }
}
