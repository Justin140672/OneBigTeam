using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Notifications.Tests;

// NOTE: the final-retry-exhausted "mark Failed" branch reads
// context?.GetJobParameter<int?>("RetryCount") from a Hangfire.Server.PerformContext, which has no
// public, test-friendly constructor (mirrors HR.Modules.Documents.Tests/ScanUploadedFileJobTests.cs,
// the sibling enqueue-style job this one was modeled on). Only the context == null path
// (retryCount defaults to 0, so isFinalAttempt is always false while MaxAttempts = 4) is exercised
// here — see SendAsync_Transient_Failure_With_Null_Context_Rethrows_Without_Marking_Failed below.
// The final-attempt-marks-Failed branch itself is not independently unit-tested for that reason;
// SanitizeFailureReason's exception-to-category mapping and the "save + rethrow" shape either side
// of that branch are otherwise fully exercised.
public class EmailDeliveryJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 6, 10, 0, 0, DateTimeKind.Utc);

    private static NotificationsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static EmailDeliveryJob BuildJob(
        NotificationsDbContext db,
        FakeEmailSender emailSender,
        FakeUserEmailReader? userEmailReader = null) =>
        new(
            db,
            emailSender,
            userEmailReader ?? new FakeUserEmailReader(),
            new FakeClock(FixedUtcNow),
            new FakeLogger<EmailDeliveryJob>());

    private static async Task<(Notification Notification, EmailDelivery Delivery)> SeedPendingDelivery(
        NotificationsDbContext db, Guid companyId, Guid notificationId)
    {
        var notification = Notification.Create(
            notificationId, companyId, Guid.NewGuid(),
            "Leave approved", "Your leave request was approved.",
            Guid.NewGuid(), DateTimeOffset.UtcNow, NotificationType.LeaveApproved);
        db.Notifications.Add(notification);

        var delivery = EmailDelivery.Create(Guid.NewGuid(), companyId, notificationId, DateTimeOffset.UtcNow);
        db.EmailDeliveries.Add(delivery);

        await db.SaveChangesAsync();
        return (notification, delivery);
    }

    [Fact]
    public async Task SendAsync_Success_Marks_Delivery_Sent_And_Calls_Email_Sender()
    {
        await using var db  = BuildContext();
        var companyId        = Guid.NewGuid();
        var notificationId   = Guid.NewGuid();
        var (notification, _) = await SeedPendingDelivery(db, companyId, notificationId);
        var emailSender       = new FakeEmailSender();
        var job                = BuildJob(db, emailSender);

        await job.SendAsync(notificationId);

        var call = Assert.Single(emailSender.Calls);
        Assert.Equal("recipient@example.test", call.ToEmail);
        Assert.Equal(notification.Title,        call.Subject);

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Sent, stored.Status);
        Assert.NotNull(stored.SentAt);
        Assert.Equal(1, stored.AttemptCount);
        Assert.NotNull(stored.LastAttemptAt);
    }

    [Fact]
    public async Task SendAsync_Transient_Failure_With_Null_Context_Rethrows_Without_Marking_Failed()
    {
        // context defaults to null (no PerformContext supplied), so retryCount defaults to 0 and
        // isFinalAttempt is always false — the delivery is left Pending (not Failed), and the
        // exception propagates so Hangfire's [AutomaticRetry] can schedule the next attempt.
        await using var db  = BuildContext();
        var companyId        = Guid.NewGuid();
        var notificationId   = Guid.NewGuid();
        await SeedPendingDelivery(db, companyId, notificationId);
        var emailSender       = new FakeEmailSender(failuresBeforeSuccess: int.MaxValue);
        var job                = BuildJob(db, emailSender);

        await Assert.ThrowsAsync<HttpRequestException>(() => job.SendAsync(notificationId));

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Pending, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Null(stored.SentAt);
        Assert.Null(stored.FailureReason);
        Assert.Empty(emailSender.Calls);
    }

    [Fact]
    public async Task SendAsync_No_Recipient_Email_Marks_Delivery_Permanently_Failed_Without_Throwing()
    {
        await using var db  = BuildContext();
        var companyId        = Guid.NewGuid();
        var notificationId   = Guid.NewGuid();
        await SeedPendingDelivery(db, companyId, notificationId);
        var emailSender       = new FakeEmailSender();
        var job                = BuildJob(db, emailSender, new FakeUserEmailReader(email: null));

        await job.SendAsync(notificationId); // must not throw

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Failed,        stored.Status);
        Assert.Equal("Invalid recipient address.",      stored.FailureReason);
        Assert.Equal(1,                                 stored.AttemptCount);
        Assert.Empty(emailSender.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_Whitespace_Only_Recipient_Email_Is_Treated_As_No_Recipient(string email)
    {
        await using var db  = BuildContext();
        var companyId        = Guid.NewGuid();
        var notificationId   = Guid.NewGuid();
        await SeedPendingDelivery(db, companyId, notificationId);
        var emailSender       = new FakeEmailSender();
        var job                = BuildJob(db, emailSender, new FakeUserEmailReader(email: email));

        await job.SendAsync(notificationId);

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Failed,   stored.Status);
        Assert.Equal("Invalid recipient address.", stored.FailureReason);
        Assert.Empty(emailSender.Calls);
    }

    [Fact]
    public async Task SendAsync_Already_Sent_Is_A_No_Op()
    {
        await using var db  = BuildContext();
        var companyId        = Guid.NewGuid();
        var notificationId   = Guid.NewGuid();
        var (_, delivery)    = await SeedPendingDelivery(db, companyId, notificationId);
        delivery.RecordAttempt(DateTimeOffset.UtcNow);
        delivery.MarkSent(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var job          = BuildJob(db, emailSender);

        await job.SendAsync(notificationId);

        Assert.Empty(emailSender.Calls);
        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(1,               stored.AttemptCount); // unchanged — no additional RecordAttempt call
        Assert.Equal(EmailDeliveryStatus.Sent, stored.Status);
    }

    [Fact]
    public async Task SendAsync_Missing_EmailDelivery_Row_Returns_Without_Throwing_Or_Calling_Email_Sender()
    {
        await using var db = BuildContext();
        var emailSender      = new FakeEmailSender();
        var job                = BuildJob(db, emailSender);

        await job.SendAsync(Guid.NewGuid()); // no EmailDelivery row exists for this id

        Assert.Empty(emailSender.Calls);
        Assert.Empty(db.EmailDeliveries);
    }

    [Fact]
    public async Task SendAsync_Missing_Notification_Marks_Delivery_Failed_Without_Throwing()
    {
        await using var db  = BuildContext();
        var companyId        = Guid.NewGuid();
        var notificationId   = Guid.NewGuid();
        // EmailDelivery row exists but its Notification does not (edge case: notification removed
        // between enqueue and job execution, e.g. RemoveBySourceEntityAsync).
        var delivery = EmailDelivery.Create(Guid.NewGuid(), companyId, notificationId, DateTimeOffset.UtcNow);
        db.EmailDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var job          = BuildJob(db, emailSender);

        await job.SendAsync(notificationId);

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Failed,        stored.Status);
        Assert.Equal("Notification no longer exists.",  stored.FailureReason);
        Assert.Empty(emailSender.Calls);
    }
}
