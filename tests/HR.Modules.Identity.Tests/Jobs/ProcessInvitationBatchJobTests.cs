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
            TestAccountCreationEmailGuard.Create(auditPublisher, Clock),
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
        var candidateReader = new FakeEmployeeInviteCandidateReader();
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
            seedDb.UserProfiles.Add(UserProfile.Create(recipient.EmployeeId, Guid.NewGuid(), Guid.Empty, recipient.Email, "Test", "User", Now));
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

        await using (var db = fixture.BuildContext())
        {
            var failingSender = new FakeInvitationEmailSender(succeeds: false);
            await BuildJob(db, candidateReader, new FakeAuditEventPublisher(), failingSender).RunAsync(batch.Id, CancellationToken.None);
        }

        await using (var verifyDb = fixture.BuildContext())
        {
            var reloaded = await verifyDb.InvitationBatchRecipients.SingleAsync(r => r.Id == recipient.Id);
            Assert.Equal(InvitationBatchRecipient.StatusFailed, reloaded.Status);
            Assert.NotNull(reloaded.InviteId);

            var inviteCount = await verifyDb.UserInvites.CountAsync(i => i.EmployeeId == recipient.EmployeeId);
            Assert.Equal(1, inviteCount);
        }

        await using (var resetDb = fixture.BuildContext())
        {
            var toReset = await resetDb.InvitationBatchRecipients.SingleAsync(r => r.Id == recipient.Id);
            toReset.ResetForRetry();
            await resetDb.SaveChangesAsync();
        }

        await using (var db = fixture.BuildContext())
        {
            var succeedingSender = new FakeInvitationEmailSender(succeeds: true);
            await BuildJob(db, candidateReader, new FakeAuditEventPublisher(), succeedingSender).RunAsync(batch.Id, CancellationToken.None);
        }

        await using var finalDb = fixture.BuildContext();
        var finalRecipient = await finalDb.InvitationBatchRecipients.SingleAsync(r => r.Id == recipient.Id);
        Assert.Equal(InvitationBatchRecipient.StatusSent, finalRecipient.Status);

        var finalInviteCount = await finalDb.UserInvites.CountAsync(i => i.EmployeeId == recipient.EmployeeId);
        Assert.Equal(1, finalInviteCount);
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

    private sealed class ThrowingForEmailInvitationEmailSender(string throwingEmail) : IInvitationEmailSender
    {
        public Task<bool> SendAsync(string toEmail, string? recipientName, string actionUrl, CancellationToken ct = default)
        {
            if (toEmail == throwingEmail)
                throw new InvalidOperationException("Simulated unexpected failure sending this recipient's email.");

            return Task.FromResult(true);
        }
    }

    // ── Ticket 9: work-email policy recheck ─────────────────────────────────────

    [Theory]
    [InlineData("queued-before-policy@gmail.com", "gmail.com")]
    [InlineData("queued-before-policy@HOTMAIL.COM", "hotmail.com")]
    public async Task RunAsync_Skips_Recipient_With_Public_Email_Without_Creating_Invite_Or_Sending(string email, string expectedDomain)
    {
        var companyId = Guid.NewGuid();
        var (batch, recipient) = await SeedWaitingRecipientAsync(companyId, email);
        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(recipient.EmployeeId, "Test Person", recipient.Email, null, null));
        var auditPublisher = new FakeAuditEventPublisher();
        var emailSender = new FakeInvitationEmailSender();

        await using var db = fixture.BuildContext();
        await BuildJob(db, candidateReader, auditPublisher, emailSender).RunAsync(batch.Id, CancellationToken.None);

        await using var verifyDb = fixture.BuildContext();
        var reloadedRecipient = await verifyDb.InvitationBatchRecipients.SingleAsync(r => r.Id == recipient.Id);
        Assert.Equal(InvitationBatchRecipient.StatusSkipped, reloadedRecipient.Status);
        Assert.Equal("PublicEmailDomain", reloadedRecipient.FailureReason);
        Assert.Null(reloadedRecipient.InviteId);

        Assert.False(await verifyDb.UserInvites.AnyAsync(i => i.EmployeeId == recipient.EmployeeId));
        Assert.Empty(emailSender.Sent);
        Assert.DoesNotContain(auditPublisher.PublishedEvents, e => e is UserInvitedAuditEvent);

        var rejection = Assert.IsType<HR.Modules.Identity.AccountCreationEmailRejectedAuditEvent>(
            Assert.Single(auditPublisher.PublishedEvents, e => e is HR.Modules.Identity.AccountCreationEmailRejectedAuditEvent));
        Assert.Equal(AuditActorType.ScheduledJob, rejection.ActorKind);
        Assert.Equal(AuditActorType.ScheduledJob, ((IAuditEvent)rejection).ActorType);
        Assert.Equal("bulk-employee-invitation", rejection.Path);
        Assert.Equal(companyId, rejection.CompanyId);
        Assert.Equal(new[] { recipient.EmployeeId }, rejection.SubjectEmployeeIds);
        Assert.Equal(new[] { expectedDomain }, rejection.Domains);
    }

    [Fact]
    public async Task RunAsync_Public_Email_Recipient_Does_Not_Block_Other_Org_Recipients_In_The_Same_Batch()
    {
        var companyId = Guid.NewGuid();
        Guid batchId;
        InvitationBatchRecipient publicRecipient;
        InvitationBatchRecipient orgRecipient;
        await using (var seed = fixture.BuildContext())
        {
            var batch = InvitationBatch.Create(companyId, Guid.NewGuid(), Now, null);
            publicRecipient = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), "mixed-public@yahoo.co.uk", Now);
            orgRecipient = InvitationBatchRecipient.Create(batch.Id, Guid.NewGuid(), $"mixed-org-{Guid.NewGuid():N}@acme.example", Now);
            seed.InvitationBatches.Add(batch);
            seed.InvitationBatchRecipients.AddRange(publicRecipient, orgRecipient);
            await seed.SaveChangesAsync();
            batchId = batch.Id;
        }

        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(publicRecipient.EmployeeId, "Public Person", publicRecipient.Email, null, null),
            new EmployeeInviteCandidate(orgRecipient.EmployeeId, "Org Person", orgRecipient.Email, null, null));
        var auditPublisher = new FakeAuditEventPublisher();
        var emailSender = new FakeInvitationEmailSender();

        await using var db = fixture.BuildContext();
        await BuildJob(db, candidateReader, auditPublisher, emailSender).RunAsync(batchId, CancellationToken.None);

        await using var verifyDb = fixture.BuildContext();
        var reloadedPublic = await verifyDb.InvitationBatchRecipients.SingleAsync(r => r.Id == publicRecipient.Id);
        var reloadedOrg = await verifyDb.InvitationBatchRecipients.SingleAsync(r => r.Id == orgRecipient.Id);
        Assert.Equal(InvitationBatchRecipient.StatusSkipped, reloadedPublic.Status);
        Assert.Equal("PublicEmailDomain", reloadedPublic.FailureReason);
        Assert.Equal(InvitationBatchRecipient.StatusSent, reloadedOrg.Status);

        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal(orgRecipient.Email, sent.ToEmail);
        Assert.False(await verifyDb.UserInvites.AnyAsync(i => i.EmployeeId == publicRecipient.EmployeeId));
        Assert.True(await verifyDb.UserInvites.AnyAsync(i => i.EmployeeId == orgRecipient.EmployeeId));
    }
}
