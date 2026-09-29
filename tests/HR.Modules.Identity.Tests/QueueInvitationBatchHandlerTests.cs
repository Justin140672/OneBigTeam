using HR.Modules.Identity.Services.AccountEmailPolicy;
using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.QueueInvitationBatch;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class QueueInvitationBatchHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);
    private static readonly FakeClock Clock = new(Now.UtcDateTime);

    private QueueInvitationBatchHandler BuildHandler(
        FakeEmployeeInviteCandidateReader candidateReader,
        RecordingBackgroundJobClient? jobClient = null,
        FakeAuditEventPublisher? auditPublisher = null) =>
        new(
            fixture.BuildContext(),
            Clock,
            candidateReader,
            jobClient ?? new RecordingBackgroundJobClient(),
            auditPublisher ?? new FakeAuditEventPublisher(),
            TestAccountCreationEmailGuard.Create(auditPublisher ?? new FakeAuditEventPublisher(), Clock));

    [Fact]
    public async Task HandleAsync_Queues_Eligible_Employees_And_Enqueues_Job()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(employeeId, "Jane Doe", "jane@test.com", null, null));
        var jobClient = new RecordingBackgroundJobClient();
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(candidateReader, jobClient, auditPublisher);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [employeeId]),
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.QueuedCount);
        Assert.Empty(result.Value.Excluded);

        await using var db = fixture.BuildContext();
        var batch = await db.InvitationBatches.SingleAsync(b => b.Id == result.Value.BatchId);
        Assert.Equal(companyId, batch.CompanyId);
        var recipients = await db.InvitationBatchRecipients.Where(r => r.BatchId == batch.Id).ToListAsync();
        Assert.Single(recipients);
        Assert.Equal(employeeId, recipients[0].EmployeeId);
        Assert.Equal("jane@test.com", recipients[0].Email);

        Assert.Single(jobClient.CreatedJobs);
        Assert.Single(auditPublisher.PublishedEvents, e => e is InvitationBatchQueuedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Employee_Not_Returned_By_Candidate_Reader()
    {
        var companyId = Guid.NewGuid();
        var eligibleId = Guid.NewGuid();
        var notEligibleId = Guid.NewGuid();
        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(eligibleId, "Eligible Person", "eligible@test.com", null, null));
        var handler = BuildHandler(candidateReader);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [eligibleId, notEligibleId]),
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.QueuedCount);
        var excluded = Assert.Single(result.Value.Excluded);
        Assert.Equal(notEligibleId, excluded.EmployeeId);
        Assert.Equal("NotEligible", excluded.Reason);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Employee_With_Existing_Account()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var eligibleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(employeeId, Guid.NewGuid(), Guid.Empty, "existing-batch-recipient@test.com", "Existing", "User", Now));
            await db.SaveChangesAsync();
        }

        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(employeeId, "Existing User", "existing-batch-recipient@test.com", null, null),
            new EmployeeInviteCandidate(eligibleId, "Eligible Person", "eligible1@test.com", null, null));
        var handler = BuildHandler(candidateReader);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [employeeId, eligibleId]),
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.QueuedCount);
        var excluded = Assert.Single(result.Value.Excluded);
        Assert.Equal(employeeId, excluded.EmployeeId);
        Assert.Equal("AlreadyHasAccount", excluded.Reason);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Employee_With_Existing_UserProfile_Account()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var eligibleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserProfiles.Add(UserProfile.Create(employeeId, Guid.NewGuid(), companyId, "profile@test.com", "Self", "Signup", Now));
            await db.SaveChangesAsync();
        }

        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(employeeId, "Self Signup", "profile@test.com", null, null),
            new EmployeeInviteCandidate(eligibleId, "Eligible Person", "eligible2@test.com", null, null));
        var handler = BuildHandler(candidateReader);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [employeeId, eligibleId]),
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.QueuedCount);
        var excluded = Assert.Single(result.Value.Excluded);
        Assert.Equal(employeeId, excluded.EmployeeId);
        Assert.Equal("AlreadyHasAccount", excluded.Reason);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Employee_With_Pending_NonExpired_Invite()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var eligibleId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(employeeId, companyId, "pending@test.com", DateTimeOffset.UtcNow) /* UserInvite.IsExpired reads the real clock, so a fixed past "Now" would eventually expire */);
            await db.SaveChangesAsync();
        }

        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(employeeId, "Pending Person", "pending@test.com", null, null),
            new EmployeeInviteCandidate(eligibleId, "Eligible Person", "eligible3@test.com", null, null));
        var handler = BuildHandler(candidateReader);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [employeeId, eligibleId]),
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var excluded = Assert.Single(result.Value.Excluded);
        Assert.Equal(employeeId, excluded.EmployeeId);
        Assert.Equal("AlreadyInvited", excluded.Reason);
    }

    [Fact]
    public async Task HandleAsync_Includes_Employee_Whose_Only_Invite_Is_Expired()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.UserInvites.Add(UserInvite.Create(employeeId, companyId, "expired@test.com", Now.AddDays(-30)));
            await db.SaveChangesAsync();
        }

        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(employeeId, "Expired Person", "expired@test.com", null, null));
        var handler = BuildHandler(candidateReader);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [employeeId]),
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.QueuedCount);
        Assert.Empty(result.Value.Excluded);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Employee_With_Missing_Email()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(employeeId, "No Email", null, null, null));
        var handler = BuildHandler(candidateReader);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [employeeId]),
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Both_Employees_Sharing_A_Duplicate_Email_Within_Same_Submission()
    {
        var companyId = Guid.NewGuid();
        var employeeId1 = Guid.NewGuid();
        var employeeId2 = Guid.NewGuid();
        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(employeeId1, "Person One", "shared@test.com", null, null),
            new EmployeeInviteCandidate(employeeId2, "Person Two", "shared@test.com", null, null));
        var handler = BuildHandler(candidateReader);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [employeeId1, employeeId2]),
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Duplicate_Email_Exclusion_Leaves_Other_Eligible_Employees_Queued()
    {
        var companyId = Guid.NewGuid();
        var employeeId1 = Guid.NewGuid();
        var employeeId2 = Guid.NewGuid();
        var eligibleId = Guid.NewGuid();
        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(employeeId1, "Person One", "shared2@test.com", null, null),
            new EmployeeInviteCandidate(employeeId2, "Person Two", "shared2@test.com", null, null),
            new EmployeeInviteCandidate(eligibleId, "Eligible Person", "eligible4@test.com", null, null));
        var handler = BuildHandler(candidateReader);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [employeeId1, employeeId2, eligibleId]),
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.QueuedCount);
        Assert.Equal(2, result.Value.Excluded.Count);
        Assert.All(result.Value.Excluded, e => Assert.Equal("DuplicateEmail", e.Reason));
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Failure_And_Creates_No_Batch_When_Zero_Eligible_Remain()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var candidateReader = new FakeEmployeeInviteCandidateReader();
        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(candidateReader, jobClient);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [employeeId]),
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(jobClient.CreatedJobs);

        await using var db = fixture.BuildContext();
        Assert.False(await db.InvitationBatches.AnyAsync(b => b.CompanyId == companyId));
    }

    [Fact]
    public async Task HandleAsync_Never_Queues_A_Cross_Company_Employee_Id()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyEmployeeId = Guid.NewGuid();
        var candidateReader = new FakeEmployeeInviteCandidateReader();
        var handler = BuildHandler(candidateReader);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [otherCompanyEmployeeId]),
            actorUserId: null,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(companyId, candidateReader.LastCompanyId);
    }

    [Fact]
    public async Task HandleAsync_Repeated_Request_With_Same_IdempotencyKey_Returns_Same_BatchId_And_Creates_No_Second_Batch()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(employeeId, "Jane Doe", "jane2@test.com", null, null));
        var jobClient = new RecordingBackgroundJobClient();
        var handler = BuildHandler(candidateReader, jobClient);
        var request = new QueueInvitationBatchRequest(companyId, [employeeId], IdempotencyKey: "same-key");

        var first = await handler.HandleAsync(request, actorUserId: null, CancellationToken.None);
        var second = await handler.HandleAsync(request, actorUserId: null, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.BatchId, second.Value.BatchId);

        await using var db = fixture.BuildContext();
        var batchCount = await db.InvitationBatches.CountAsync(b => b.CompanyId == companyId);
        Assert.Equal(1, batchCount);

        Assert.Single(jobClient.CreatedJobs);
    }

    // ── Ticket 9: work-email policy ─────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Mixed_Batch_Queues_Org_Email_And_Excludes_Public_Email()
    {
        var companyId = Guid.NewGuid();
        var orgEmployeeId = Guid.NewGuid();
        var publicEmployeeId = Guid.NewGuid();
        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(orgEmployeeId, "Org Person", "org.person@acme.example", null, null),
            new EmployeeInviteCandidate(publicEmployeeId, "Public Person", "public.person@gmail.com", null, null));
        var jobClient = new RecordingBackgroundJobClient();
        var auditPublisher = new FakeAuditEventPublisher();
        var actorId = Guid.NewGuid();
        var handler = BuildHandler(candidateReader, jobClient, auditPublisher);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [orgEmployeeId, publicEmployeeId]),
            actorId,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.QueuedCount);
        var excluded = Assert.Single(result.Value.Excluded);
        Assert.Equal(publicEmployeeId, excluded.EmployeeId);
        Assert.Equal(AccountCreationEmailGuard.BulkExclusionReason, excluded.Reason);
        Assert.Equal("PublicEmailDomain", excluded.Reason);
        Assert.Equal("public.person@gmail.com", excluded.Email);

        await using var db = fixture.BuildContext();
        var recipient = Assert.Single(await db.InvitationBatchRecipients.Where(r => r.BatchId == result.Value.BatchId).ToListAsync());
        Assert.Equal(orgEmployeeId, recipient.EmployeeId);
        Assert.False(await db.InvitationBatchRecipients.AnyAsync(r => r.EmployeeId == publicEmployeeId));

        Assert.Single(jobClient.CreatedJobs);

        var rejection = Assert.IsType<AccountCreationEmailRejectedAuditEvent>(
            Assert.Single(auditPublisher.PublishedEvents, e => e is AccountCreationEmailRejectedAuditEvent));
        Assert.Equal("bulk-employee-invitation", rejection.Path);
        Assert.Equal(new[] { publicEmployeeId }, rejection.SubjectEmployeeIds);
        Assert.Equal(new[] { "gmail.com" }, rejection.Domains);
        Assert.Equal(1, rejection.RejectedCount);
        Assert.Equal(actorId, rejection.ActorUserId);
        Assert.Single(auditPublisher.PublishedEvents, e => e is InvitationBatchQueuedAuditEvent);
    }

    [Fact]
    public async Task HandleAsync_All_Public_Batch_Fails_With_WorkEmailRequired_Listing_Each_Address_And_Creates_Nothing()
    {
        var companyId = Guid.NewGuid();
        var gmailId = Guid.NewGuid();
        var hotmailId = Guid.NewGuid();
        var yahooId = Guid.NewGuid();
        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(gmailId, "Gmail Person", "gmail.person@gmail.com", null, null),
            new EmployeeInviteCandidate(hotmailId, "Hotmail Person", "hotmail.person@hotmail.co.uk", null, null),
            new EmployeeInviteCandidate(yahooId, "Yahoo Person", "yahoo.person@YAHOO.COM", null, null));
        var jobClient = new RecordingBackgroundJobClient();
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(candidateReader, jobClient, auditPublisher);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [gmailId, hotmailId, yahooId]),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountCreationEmailGuard.WorkEmailRequiredCode, result.Error.Code);
        Assert.Contains("gmail.person@gmail.com", result.Error.Message);
        Assert.Contains("hotmail.person@hotmail.co.uk", result.Error.Message);
        Assert.Contains("yahoo.person@YAHOO.COM", result.Error.Message);

        await using var db = fixture.BuildContext();
        Assert.False(await db.InvitationBatches.AnyAsync(b => b.CompanyId == companyId));
        Assert.False(await db.InvitationBatchRecipients.AnyAsync(r =>
            r.EmployeeId == gmailId || r.EmployeeId == hotmailId || r.EmployeeId == yahooId));
        Assert.Empty(jobClient.CreatedJobs);

        var rejection = Assert.IsType<AccountCreationEmailRejectedAuditEvent>(Assert.Single(auditPublisher.PublishedEvents));
        Assert.Equal(3, rejection.RejectedCount);
        Assert.Equal(new[] { "gmail.com", "hotmail.co.uk", "yahoo.com" }, rejection.Domains);
    }

    [Fact]
    public async Task HandleAsync_No_Eligible_Recipients_For_Non_Policy_Reasons_Keeps_Plain_Validation_Error()
    {
        // Negative branch of the all-rejected message: when nobody was rejected by the email
        // policy the original validation error (not work_email_required) is returned.
        var candidateReader = new FakeEmployeeInviteCandidateReader();
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(candidateReader, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(Guid.NewGuid(), [Guid.NewGuid()]),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(auditPublisher.PublishedEvents);
    }

    [Fact]
    public async Task HandleAsync_Malformed_Work_Email_Is_Excluded_As_PublicEmailDomain()
    {
        var companyId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var malformedId = Guid.NewGuid();
        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(orgId, "Org Person", "org.person2@acme.example", null, null),
            new EmployeeInviteCandidate(malformedId, "Malformed Person", "malformed@localhost", null, null));
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(candidateReader, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [orgId, malformedId]),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var excluded = Assert.Single(result.Value.Excluded);
        Assert.Equal(malformedId, excluded.EmployeeId);
        Assert.Equal("PublicEmailDomain", excluded.Reason);

        var rejection = Assert.IsType<AccountCreationEmailRejectedAuditEvent>(
            Assert.Single(auditPublisher.PublishedEvents, e => e is AccountCreationEmailRejectedAuditEvent));
        Assert.Equal(new[] { "(malformed)" }, rejection.Domains);
    }

    [Fact]
    public async Task HandleAsync_All_Org_Batch_Publishes_No_Rejection_Audit()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var candidateReader = new FakeEmployeeInviteCandidateReader(
            new EmployeeInviteCandidate(employeeId, "Org Person", "org.person3@brightsparks-consulting.co.uk", null, null));
        var auditPublisher = new FakeAuditEventPublisher();
        var handler = BuildHandler(candidateReader, auditPublisher: auditPublisher);

        var result = await handler.HandleAsync(
            new QueueInvitationBatchRequest(companyId, [employeeId]),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Excluded);
        Assert.DoesNotContain(auditPublisher.PublishedEvents, e => e is AccountCreationEmailRejectedAuditEvent);
    }
}
