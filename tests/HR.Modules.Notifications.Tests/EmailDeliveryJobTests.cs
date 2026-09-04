using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications;
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
// of that branch are otherwise fully exercised. NOT-05: consequently the isFinalAttempt branch's
// EmailDeliveryFailedAuditEvent publish is also not directly exercised here — the "no recipient
// email" immediate-fail path (SendAsync_No_Recipient_Email_...) exercises the exact same publish
// call/shape via the sibling immediate-fail branch instead.
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
        FakeUserEmailReader? userEmailReader = null,
        FakeAuditPublisher? auditPublisher = null,
        FakeCompanyNotificationSettingsReader? notificationSettingsReader = null,
        CapturingAdministrativeAlertWriter? administrativeAlertWriter = null) =>
        new(
            db,
            emailSender,
            userEmailReader ?? new FakeUserEmailReader(),
            new FakeClock(FixedUtcNow),
            auditPublisher ?? new FakeAuditPublisher(),
            administrativeAlertWriter ?? new CapturingAdministrativeAlertWriter(),
            notificationSettingsReader ?? new FakeCompanyNotificationSettingsReader(),
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

        await job.SendAsync(notificationId, companyId);

        var call = Assert.Single(emailSender.Calls);
        Assert.Equal("recipient@example.test", call.ToEmail);
        Assert.Equal(notification.Title,        call.Subject);

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Sent, stored.Status);
        Assert.NotNull(stored.SentAt);
        Assert.Equal(1, stored.AttemptCount);
        Assert.NotNull(stored.LastAttemptAt);
    }

    // OBT-REM-11: the caller-supplied companyId (used to scope the Hangfire failure audit to a
    // tenant) must be verified against the entity actually loaded, so a caller cannot enqueue a
    // job whose job-argument company id disagrees with the delivery it operates on.
    [Fact]
    public async Task SendAsync_Throws_When_Supplied_CompanyId_Does_Not_Match_Delivery()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        await SeedPendingDelivery(db, companyId, notificationId);
        var emailSender = new FakeEmailSender();
        var job          = BuildJob(db, emailSender);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.SendAsync(notificationId, otherCompanyId));
        Assert.Empty(emailSender.Calls);
    }

    // NOT-05: audit -------------------------------------------------------------------------------

    [Fact]
    public async Task SendAsync_Success_Publishes_EmailDeliverySucceededAuditEvent()
    {
        await using var db  = BuildContext();
        var companyId        = Guid.NewGuid();
        var notificationId   = Guid.NewGuid();
        var (notification, _) = await SeedPendingDelivery(db, companyId, notificationId);
        var emailSender       = new FakeEmailSender();
        var auditPublisher    = new FakeAuditPublisher();
        var job                = BuildJob(db, emailSender, auditPublisher: auditPublisher);

        await job.SendAsync(notificationId, companyId);

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        var evt = Assert.Single(auditPublisher.Published);
        var succeeded = Assert.IsType<EmailDeliverySucceededAuditEvent>(evt);
        Assert.Equal(companyId,                    succeeded.CompanyId);
        Assert.Equal(notificationId,                succeeded.NotificationId);
        Assert.Equal(notification.EmployeeId,       succeeded.RecipientEmployeeId);
        Assert.Equal(stored.SentAt,                 succeeded.SentAt);
        Assert.Equal(NotificationsSystemActor.Id,   ((HR.SharedKernel.IAuditEvent)succeeded).ActorEmployeeId);
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
        var auditPublisher    = new FakeAuditPublisher();
        var job                = BuildJob(db, emailSender, auditPublisher: auditPublisher);

        await Assert.ThrowsAsync<HttpRequestException>(() => job.SendAsync(notificationId, companyId));

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Pending, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Null(stored.SentAt);
        Assert.Null(stored.FailureReason);
        Assert.Empty(emailSender.Calls);
        // NOT-05: a non-final retry attempt must not publish a failure event — only the exhausted
        // final attempt does, so a delivery that eventually succeeds after retries never shows a
        // misleading failure event in its audit history.
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task SendAsync_No_Recipient_Email_Marks_Delivery_Permanently_Failed_Without_Throwing()
    {
        await using var db  = BuildContext();
        var companyId        = Guid.NewGuid();
        var notificationId   = Guid.NewGuid();
        await SeedPendingDelivery(db, companyId, notificationId);
        var emailSender       = new FakeEmailSender();
        var auditPublisher    = new FakeAuditPublisher();
        var job                = BuildJob(db, emailSender, new FakeUserEmailReader(email: null), auditPublisher);

        await job.SendAsync(notificationId, companyId); // must not throw

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Failed,        stored.Status);
        Assert.Equal("Invalid recipient address.",      stored.FailureReason);
        Assert.Equal(1,                                 stored.AttemptCount);
        Assert.Empty(emailSender.Calls);

        var evt = Assert.Single(auditPublisher.Published);
        var failed = Assert.IsType<EmailDeliveryFailedAuditEvent>(evt);
        Assert.Equal(companyId,                     failed.CompanyId);
        Assert.Equal(notificationId,                 failed.NotificationId);
        Assert.Equal("Invalid recipient address.",   failed.SanitizedFailureReason);
        Assert.DoesNotContain(nameof(HttpRequestException), failed.SanitizedFailureReason);
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

        await job.SendAsync(notificationId, companyId);

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
        var auditPublisher = new FakeAuditPublisher();
        var job          = BuildJob(db, emailSender, auditPublisher: auditPublisher);

        await job.SendAsync(notificationId, companyId);

        Assert.Empty(emailSender.Calls);
        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(1,               stored.AttemptCount); // unchanged — no additional RecordAttempt call
        Assert.Equal(EmailDeliveryStatus.Sent, stored.Status);
        // NOT-05: replayed/re-enqueued jobs for an already-delivered notification must not publish a
        // second, misleading success event.
        Assert.Empty(auditPublisher.Published);
    }

    [Fact]
    public async Task SendAsync_Missing_EmailDelivery_Row_Returns_Without_Throwing_Or_Calling_Email_Sender()
    {
        await using var db = BuildContext();
        var emailSender      = new FakeEmailSender();
        var job                = BuildJob(db, emailSender);

        await job.SendAsync(Guid.NewGuid(), Guid.NewGuid()); // no EmailDelivery row exists for this id

        Assert.Empty(emailSender.Calls);
        Assert.Empty(db.EmailDeliveries);
    }

    // NOT-03: templated vs non-templated wording -----------------------------------------------

    [Fact]
    public async Task SendAsync_Templated_Delivery_Sends_Its_Own_Rendered_Subject_And_Body()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();

        // Notification's own Title/Body deliberately differ from the templated delivery's rendered
        // subject/body, so this test can distinguish "sent the delivery's own content" from
        // "re-wrapped Notification.Title/Body".
        var notification = Notification.Create(
            notificationId, companyId, Guid.NewGuid(),
            "Notification title (should not be sent)", "Notification body (should not be sent)",
            Guid.NewGuid(), DateTimeOffset.UtcNow, NotificationType.LeaveApproved);
        db.Notifications.Add(notification);

        var delivery = EmailDelivery.CreateTemplated(
            Guid.NewGuid(), companyId, notificationId, templateVersion: 1,
            emailSubject: "Your leave request has been approved",
            emailBody: "<html><body><p>Your leave from 3 Aug 2026 to 7 Aug 2026 has been approved.</p></body></html>",
            now: DateTimeOffset.UtcNow);
        db.EmailDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var job = BuildJob(db, emailSender);

        await job.SendAsync(notificationId, companyId);

        var call = Assert.Single(emailSender.Calls);
        Assert.Equal("Your leave request has been approved", call.Subject);
        Assert.Equal(
            "<html><body><p>Your leave from 3 Aug 2026 to 7 Aug 2026 has been approved.</p></body></html>",
            call.HtmlBody);

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Sent, stored.Status);
    }

    [Fact]
    public async Task SendAsync_NonTemplated_Delivery_Falls_Back_To_BuildHtmlBody_With_Notification_Title_And_Body()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var (notification, delivery) = await SeedPendingDelivery(db, companyId, notificationId);

        Assert.Null(delivery.TemplateVersion);
        Assert.Null(delivery.EmailSubject);
        Assert.Null(delivery.EmailBody);

        var emailSender = new FakeEmailSender();
        var job = BuildJob(db, emailSender);

        await job.SendAsync(notificationId, companyId);

        var call = Assert.Single(emailSender.Calls);
        Assert.Equal(notification.Title, call.Subject);
        Assert.Contains(notification.Title, call.HtmlBody);
        Assert.Contains(notification.Body!, call.HtmlBody);
    }

    // SET-06: notification-channel settings ----------------------------------------------------

    [Fact]
    public async Task SendAsync_EmailNotificationsEnabled_False_For_NonMandatory_Type_Marks_Delivery_Skipped_Without_Sending()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        await SeedPendingDelivery(db, companyId, notificationId); // LeaveApproved — non-mandatory
        var emailSender = new FakeEmailSender();
        var settingsReader = new FakeCompanyNotificationSettingsReader(
            new HR.Infrastructure.Abstractions.CompanyNotificationSettings(false, true));
        var job = BuildJob(db, emailSender, notificationSettingsReader: settingsReader);

        await job.SendAsync(notificationId, companyId); // must not throw

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Skipped, stored.Status);
        Assert.NotEqual(EmailDeliveryStatus.Failed, stored.Status);
        Assert.NotEqual(EmailDeliveryStatus.Sent, stored.Status);
        Assert.Equal("Email notifications disabled for this company.", stored.FailureReason);
        Assert.Empty(emailSender.Calls);
    }

    [Fact]
    public async Task SendAsync_EmailNotificationsEnabled_False_For_Mandatory_Type_Still_Sends_Normally()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var notification = Notification.Create(
            notificationId, companyId, Guid.NewGuid(),
            "Document expired", "Your document has expired.",
            Guid.NewGuid(), DateTimeOffset.UtcNow, NotificationType.DocumentExpired);
        db.Notifications.Add(notification);
        var delivery = EmailDelivery.Create(Guid.NewGuid(), companyId, notificationId, DateTimeOffset.UtcNow);
        db.EmailDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var settingsReader = new FakeCompanyNotificationSettingsReader(
            new HR.Infrastructure.Abstractions.CompanyNotificationSettings(false, true));
        var job = BuildJob(db, emailSender, notificationSettingsReader: settingsReader);

        await job.SendAsync(notificationId, companyId);

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Sent, stored.Status);
        Assert.Single(emailSender.Calls);
    }

    [Fact]
    public async Task SendAsync_Delivery_Queued_While_Enabled_Then_Disabled_Before_Job_Runs_Is_Skipped_Not_Sent()
    {
        // Configuration-changed-after-queueing: the delivery was created while
        // EmailNotificationsEnabled was true (simulated by the plain SeedPendingDelivery helper,
        // which mirrors what NotificationWriter would have persisted at queue time), but by the
        // time this job actually runs, the setting has been switched off.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        await SeedPendingDelivery(db, companyId, notificationId);
        var emailSender = new FakeEmailSender();
        var settingsReader = new FakeCompanyNotificationSettingsReader(
            new HR.Infrastructure.Abstractions.CompanyNotificationSettings(false, true));
        var job = BuildJob(db, emailSender, notificationSettingsReader: settingsReader);

        await job.SendAsync(notificationId, companyId);

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Skipped, stored.Status);
        Assert.Empty(emailSender.Calls);
    }

    [Fact]
    public async Task SendAsync_EmailNotificationsEnabled_True_Sends_Normally_Regression()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        await SeedPendingDelivery(db, companyId, notificationId);
        var emailSender = new FakeEmailSender();
        var settingsReader = new FakeCompanyNotificationSettingsReader(
            new HR.Infrastructure.Abstractions.CompanyNotificationSettings(true, true));
        var job = BuildJob(db, emailSender, notificationSettingsReader: settingsReader);

        await job.SendAsync(notificationId, companyId);

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Sent, stored.Status);
        Assert.Single(emailSender.Calls);
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

        await job.SendAsync(notificationId, companyId);

        var stored = await db.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        Assert.Equal(EmailDeliveryStatus.Failed,        stored.Status);
        Assert.Equal("Notification no longer exists.",  stored.FailureReason);
        Assert.Empty(emailSender.Calls);
    }

    // ADM-03: permanent delivery failures surface in the administrative alerts inbox --------------

    [Fact]
    public async Task SendAsync_No_Recipient_Email_Raises_Exactly_One_IntegrationDelivery_Alert()
    {
        await using var db  = BuildContext();
        var companyId        = Guid.NewGuid();
        var notificationId   = Guid.NewGuid();
        await SeedPendingDelivery(db, companyId, notificationId);
        var alertWriter       = new CapturingAdministrativeAlertWriter();
        var job                = BuildJob(db, new FakeEmailSender(), new FakeUserEmailReader(email: null),
            administrativeAlertWriter: alertWriter);

        await job.SendAsync(notificationId, companyId);

        var command = Assert.Single(alertWriter.Commands);
        Assert.Equal(companyId, command.CompanyId);
        Assert.Equal(AdministrativeAlertCategory.IntegrationDelivery, command.Category);
        Assert.Equal(AdministrativeAlertSeverity.Warning, command.Severity);
        Assert.Equal("integration:email-delivery-failure", command.DedupKey);
        Assert.Equal(notificationId, command.AffectedEntityId);
    }

    [Fact]
    public async Task SendAsync_Transient_Retryable_Failure_Does_Not_Raise_An_Administrative_Alert()
    {
        await using var db  = BuildContext();
        var companyId        = Guid.NewGuid();
        var notificationId   = Guid.NewGuid();
        await SeedPendingDelivery(db, companyId, notificationId);
        var alertWriter       = new CapturingAdministrativeAlertWriter();
        var job                = BuildJob(db, new FakeEmailSender(failuresBeforeSuccess: int.MaxValue),
            administrativeAlertWriter: alertWriter);

        await Assert.ThrowsAsync<HttpRequestException>(() => job.SendAsync(notificationId, companyId));

        Assert.Empty(alertWriter.Commands);
    }

    // OBT-REM-12: the "claim" step's DbUpdateConcurrencyException guard --------------------------

    [Fact]
    public async Task SendAsync_Concurrency_Conflict_On_Claim_Step_Backs_Off_Without_Sending()
    {
        // Two separate DbContext instances pointed at the same InMemory database name, sharing the
        // same EmailDelivery row's xmin-backed concurrency token (see EmailDeliveryConfiguration):
        // the first context "wins" the claim by saving first (bumping the store's current token
        // value), so when the second context — the one this job runs against — tries to persist its
        // own RecordAttempt() with its now-stale original token value, EF Core raises
        // DbUpdateConcurrencyException exactly as it would for a real overlapping Hangfire execution.
        var dbName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var companyId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();

        await using (var seedDb = new NotificationsDbContext(options))
        {
            await SeedPendingDelivery(seedDb, companyId, notificationId);
        }

        // staleLoadContext loads (and tracks) the row FIRST, capturing the pre-conflict concurrency
        // token as its "original value" — this is the context/job under test.
        await using var staleLoadContext = new NotificationsDbContext(options);
        await staleLoadContext.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);

        // Context A then loads its own, independent copy and saves first, bumping the concurrency
        // token's current store value — simulating another execution "winning" the claim race.
        // EF Core's InMemory provider does not auto-generate a new xmin-shaped value the way
        // PostgreSQL does on every UPDATE, so the shadow token is bumped explicitly here to make the
        // InMemory provider surface the same DbUpdateConcurrencyException a real overlapping Hangfire
        // execution would get from Npgsql's real xmin system column.
        await using (var contextA = new NotificationsDbContext(options))
        {
            var deliveryA = await contextA.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
            deliveryA.RecordAttempt(FixedUtcNow);
            var currentToken = (uint)contextA.Entry(deliveryA).Property("xmin").CurrentValue!;
            contextA.Entry(deliveryA).Property("xmin").CurrentValue = currentToken + 1;
            await contextA.SaveChangesAsync();
        }

        var emailSender = new FakeEmailSender();
        var logger = new FakeLogger<EmailDeliveryJob>();
        var job = new EmailDeliveryJob(
            staleLoadContext,
            emailSender,
            new FakeUserEmailReader(),
            new FakeClock(FixedUtcNow),
            new FakeAuditPublisher(),
            new CapturingAdministrativeAlertWriter(),
            new FakeCompanyNotificationSettingsReader(),
            logger);

        // staleLoadContext's tracked copy of the row still carries the pre-contextA-save concurrency
        // token in its OriginalValues, so this SendAsync's own RecordAttempt()+SaveChangesAsync should
        // be rejected as a concurrency conflict rather than overwrite context A's already-persisted
        // attempt.
        await job.SendAsync(notificationId, companyId); // must not throw — caught and treated as a no-op

        Assert.Empty(emailSender.Calls);

        await using var verifyDb = new NotificationsDbContext(options);
        var stored = await verifyDb.EmailDeliveries.SingleAsync(d => d.NotificationId == notificationId);
        // Context A's attempt (the "winner") is the one that persisted.
        Assert.Equal(1, stored.AttemptCount);
        Assert.Equal(EmailDeliveryStatus.Pending, stored.Status);
    }
}
