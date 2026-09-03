using HR.Modules.Support.Domain;
using HR.Modules.Support.Jobs;
using HR.Modules.Support.Persistence;
using HR.Modules.Support.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Tests;

/// <summary>
/// TEST-005. Failure-safety / idempotency / retry-terminal coverage for
/// <see cref="SupportNotificationRetryJob"/>. No real Postmark — <see cref="FakeEmailSender"/>
/// stands in and can simulate a provider outage.
/// Note: the job takes no <c>ILogger</c>, so there is no "PII not logged" assertion to make —
/// the only recipient data it handles is written to <c>SupportNotificationAttempt.RecipientEmail</c>,
/// which is the intended store for it.
/// </summary>
public class SupportNotificationRetryJobTests
{
    private const int MaxRetryCount = 5;
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset SeedNow = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private static SupportDbContext BuildContext(string? name = null) =>
        new(new DbContextOptionsBuilder<SupportDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString("N"))
            .Options);

    private static SupportRequest SeedRequest(Guid companyId, string reference = "SUP-2026-0001")
        => SupportRequest.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), null,
            SupportRequestType.AskQuestion, "Title", "Description", SupportRequestPriority.Low,
            reference, null, null, null, false, null, null, SeedNow);

    private static SupportNotificationAttempt SeedFailedAttempt(
        Guid companyId, Guid supportRequestId, string email = "customer@example.test", int retryCount = 0)
    {
        var attempt = SupportNotificationAttempt.Create(
            Guid.NewGuid(), supportRequestId, companyId,
            SupportNotificationType.StaffReplyCustomerNotification, email, SeedNow);
        attempt.MarkFailed("Initial send failed.", SeedNow);
        for (var i = 0; i < retryCount; i++)
            attempt.IncrementRetry();
        return attempt;
    }

    private static SupportNotificationRetryJob BuildJob(SupportDbContext db, FakeEmailSender email) =>
        new(db, email, new FakeClock(FixedUtcNow));

    [Fact]
    public async Task First_Run_Retries_A_Failed_Attempt_Sends_One_Email_And_Marks_It_Sent()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = SeedRequest(companyId);
        var attempt = SeedFailedAttempt(companyId, request.Id);
        db.SupportRequests.Add(request);
        db.SupportNotificationAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var email = new FakeEmailSender();
        await BuildJob(db, email).ExecuteAsync();

        var saved = await db.SupportNotificationAttempts.SingleAsync();
        Assert.Equal(SupportNotificationStatus.Sent, saved.Status);
        Assert.Equal(1, saved.RetryCount);
        Assert.Null(saved.ErrorMessage);
        Assert.Single(email.Sent);
        Assert.Equal("customer@example.test", email.Sent.Single().ToEmail);
    }

    [Fact]
    public async Task Running_Twice_In_A_Row_Does_Not_Send_A_Second_Email()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = SeedRequest(companyId);
        db.SupportRequests.Add(request);
        db.SupportNotificationAttempts.Add(SeedFailedAttempt(companyId, request.Id));
        await db.SaveChangesAsync();

        var email = new FakeEmailSender();
        var job = BuildJob(db, email);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        Assert.Single(email.Sent);
        var saved = await db.SupportNotificationAttempts.SingleAsync();
        Assert.Equal(SupportNotificationStatus.Sent, saved.Status);
        Assert.Equal(1, saved.RetryCount);
    }

    [Fact]
    public async Task An_Already_Sent_Attempt_Is_Never_Picked_Up_Again()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = SeedRequest(companyId);
        var attempt = SeedFailedAttempt(companyId, request.Id);
        attempt.MarkSent(SeedNow);
        db.SupportRequests.Add(request);
        db.SupportNotificationAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var email = new FakeEmailSender();
        await BuildJob(db, email).ExecuteAsync();

        Assert.Empty(email.Sent);
    }

    [Fact]
    public async Task Retry_After_A_Provider_Outage_Leaves_State_Intact_And_Succeeds_On_The_Next_Run()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = SeedRequest(companyId);
        db.SupportRequests.Add(request);
        db.SupportNotificationAttempts.Add(SeedFailedAttempt(companyId, request.Id));
        await db.SaveChangesAsync();

        var email = new FakeEmailSender { ThrowOnSend = true };
        var job = BuildJob(db, email);

        await job.ExecuteAsync(); // provider down

        var afterOutage = await db.SupportNotificationAttempts.SingleAsync();
        Assert.Equal(SupportNotificationStatus.Failed, afterOutage.Status);
        Assert.Equal(1, afterOutage.RetryCount);
        Assert.Contains("Simulated email provider failure", afterOutage.ErrorMessage);
        Assert.Empty(email.Sent);

        email.ThrowOnSend = false;
        await job.ExecuteAsync(); // provider recovered

        var recovered = await db.SupportNotificationAttempts.SingleAsync();
        Assert.Equal(SupportNotificationStatus.Sent, recovered.Status);
        Assert.Equal(2, recovered.RetryCount);
        Assert.Single(email.Sent);
    }

    [Fact]
    public async Task Attempt_With_Missing_Recipient_Email_Is_Marked_Failed_Without_Throwing_Or_Sending()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = SeedRequest(companyId);
        db.SupportRequests.Add(request);
        db.SupportNotificationAttempts.Add(SeedFailedAttempt(companyId, request.Id, email: "   "));
        await db.SaveChangesAsync();

        var email = new FakeEmailSender();
        var ex = await Record.ExceptionAsync(() => BuildJob(db, email).ExecuteAsync());

        Assert.Null(ex);
        Assert.Empty(email.Sent);
        var saved = await db.SupportNotificationAttempts.SingleAsync();
        Assert.Equal(SupportNotificationStatus.Failed, saved.Status);
        Assert.Equal(1, saved.RetryCount);
        Assert.Contains("No recipient email", saved.ErrorMessage);
    }

    [Fact]
    public async Task Attempt_Whose_Support_Request_No_Longer_Exists_Is_Marked_Failed_Gracefully()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        // No SupportRequest row seeded for this id.
        db.SupportNotificationAttempts.Add(SeedFailedAttempt(companyId, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var email = new FakeEmailSender();
        var ex = await Record.ExceptionAsync(() => BuildJob(db, email).ExecuteAsync());

        Assert.Null(ex);
        Assert.Empty(email.Sent);
        var saved = await db.SupportNotificationAttempts.SingleAsync();
        Assert.Equal(SupportNotificationStatus.Failed, saved.Status);
        Assert.Contains("no longer exists", saved.ErrorMessage);
    }

    [Fact]
    public async Task An_Attempt_That_Has_Reached_The_Max_Retry_Count_Is_Terminal_And_Never_Retried_Again()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = SeedRequest(companyId);
        db.SupportRequests.Add(request);
        db.SupportNotificationAttempts.Add(SeedFailedAttempt(companyId, request.Id, retryCount: MaxRetryCount));
        await db.SaveChangesAsync();

        var email = new FakeEmailSender();
        await BuildJob(db, email).ExecuteAsync();

        Assert.Empty(email.Sent);
        var saved = await db.SupportNotificationAttempts.SingleAsync();
        Assert.Equal(SupportNotificationStatus.Failed, saved.Status);
        Assert.Equal(MaxRetryCount, saved.RetryCount); // untouched
    }

    [Fact]
    public async Task An_Attempt_On_Its_Final_Allowed_Retry_Transitions_To_Terminal_After_One_More_Failure()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = SeedRequest(companyId);
        db.SupportRequests.Add(request);
        db.SupportNotificationAttempts.Add(SeedFailedAttempt(companyId, request.Id, retryCount: MaxRetryCount - 1));
        await db.SaveChangesAsync();

        var email = new FakeEmailSender { ThrowOnSend = true };
        var job = BuildJob(db, email);

        await job.ExecuteAsync(); // 4 -> 5, fails again
        var afterFinalRetry = await db.SupportNotificationAttempts.SingleAsync();
        Assert.Equal(MaxRetryCount, afterFinalRetry.RetryCount);
        Assert.Equal(SupportNotificationStatus.Failed, afterFinalRetry.Status);

        email.ThrowOnSend = false;
        await job.ExecuteAsync(); // now excluded by the RetryCount < Max guard

        Assert.Empty(email.Sent);
        var terminal = await db.SupportNotificationAttempts.SingleAsync();
        Assert.Equal(MaxRetryCount, terminal.RetryCount);
        Assert.Equal(SupportNotificationStatus.Failed, terminal.Status);
    }

    [Fact]
    public async Task Every_Processed_Attempt_Retains_Its_Own_Tenant_CompanyId()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var requestA = SeedRequest(companyA, "SUP-2026-000A");
        var requestB = SeedRequest(companyB, "SUP-2026-000B");
        var attemptA = SeedFailedAttempt(companyA, requestA.Id, "a@example.test");
        var attemptB = SeedFailedAttempt(companyB, requestB.Id, "b@example.test");
        db.SupportRequests.AddRange(requestA, requestB);
        db.SupportNotificationAttempts.AddRange(attemptA, attemptB);
        await db.SaveChangesAsync();

        var email = new FakeEmailSender();
        await BuildJob(db, email).ExecuteAsync();

        var savedA = await db.SupportNotificationAttempts.SingleAsync(a => a.Id == attemptA.Id);
        var savedB = await db.SupportNotificationAttempts.SingleAsync(a => a.Id == attemptB.Id);
        Assert.Equal(companyA, savedA.CompanyId);
        Assert.Equal(companyB, savedB.CompanyId);
        Assert.Equal(SupportNotificationStatus.Sent, savedA.Status);
        Assert.Equal(SupportNotificationStatus.Sent, savedB.Status);
        Assert.Equal(2, email.Sent.Count);
    }

    [Fact]
    public async Task Pending_And_Sent_Attempts_Are_Ignored_Only_Failed_Ones_Are_Retried()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = SeedRequest(companyId);
        db.SupportRequests.Add(request);

        var pending = SupportNotificationAttempt.Create(
            Guid.NewGuid(), request.Id, companyId,
            SupportNotificationType.NewRequestAdminAlert, "pending@example.test", SeedNow);
        var failed = SeedFailedAttempt(companyId, request.Id, "failed@example.test");
        db.SupportNotificationAttempts.AddRange(pending, failed);
        await db.SaveChangesAsync();

        var email = new FakeEmailSender();
        await BuildJob(db, email).ExecuteAsync();

        Assert.Single(email.Sent);
        Assert.Equal("failed@example.test", email.Sent.Single().ToEmail);
    }
}
