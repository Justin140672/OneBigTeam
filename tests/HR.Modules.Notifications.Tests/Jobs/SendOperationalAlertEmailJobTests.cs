using HR.Infrastructure.Abstractions;
using HR.Modules.Notifications;
using HR.Modules.Notifications.Domain;
using HR.Modules.Notifications.Jobs;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Notifications.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HR.Modules.Notifications.Tests.Jobs;

public class SendOperationalAlertEmailJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 9, 10, 0, 0, DateTimeKind.Utc);
    private const string Recipient = "ops@internal.example";
    private const string AdminBaseUrl = "https://admin.example";
    private const string SecretDetail = "Missing file: april-2026-payslip-john-smith.pdf";

    private static NotificationsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SendOperationalAlertEmailJob BuildJob(
        NotificationsDbContext db,
        FakeEmailSender emailSender,
        string? recipient = Recipient,
        string? adminBaseUrl = AdminBaseUrl) =>
        new(
            db,
            emailSender,
            Options.Create(new OperationalAlertEmailOptions
            {
                InternalRecipientEmail = recipient,
                AdminAppBaseUrl = adminBaseUrl,
            }),
            new FakeClock(FixedUtcNow),
            new FakeLogger<SendOperationalAlertEmailJob>());

    private static RaiseAdministrativeAlertCommand Command(Guid companyId, Guid exportId, int missingCount) =>
        new(
            companyId,
            AdministrativeAlertSeverity.Warning,
            AdministrativeAlertCategory.ReportGeneration,
            "Organisation data export completed with missing files",
            SecretDetail,
            new DateTimeOffset(FixedUtcNow),
            $"report:export-missing-files:{exportId}",
            "OrganisationDataExport",
            exportId,
            "Investigate storage",
            null,
            missingCount);

    private static async Task<(Guid AlertId, Guid CompanyId, Guid ExportId)> SeedAlertWithDelivery(
        NotificationsDbContext db, int missingCount = 4)
    {
        var companyId = Guid.NewGuid();
        var exportId = Guid.NewGuid();
        var alert = AdministrativeAlert.Raise(Guid.NewGuid(), Command(companyId, exportId, missingCount), new DateTimeOffset(FixedUtcNow));
        db.AdministrativeAlerts.Add(alert);
        db.OperationalAlertEmailDeliveries.Add(
            OperationalAlertEmailDelivery.Create(Guid.NewGuid(), alert.Id, companyId, new DateTimeOffset(FixedUtcNow)));
        await db.SaveChangesAsync();
        return (alert.Id, companyId, exportId);
    }

    [Fact]
    public async Task Happy_Path_Sends_One_Email_Marks_Delivery_Sent_And_Omits_Sensitive_Detail()
    {
        await using var db = BuildContext();
        var (alertId, companyId, exportId) = await SeedAlertWithDelivery(db, missingCount: 37);
        var emailSender = new FakeEmailSender();
        var job = BuildJob(db, emailSender);

        await job.SendAsync(alertId);

        var call = Assert.Single(emailSender.Calls);
        Assert.Equal(Recipient, call.ToEmail);
        Assert.Contains(companyId.ToString(), call.HtmlBody);
        Assert.Contains(exportId.ToString(), call.HtmlBody);
        Assert.Contains("37", call.HtmlBody);
        Assert.Contains($"{AdminBaseUrl}/operational-alerts/{alertId}", call.HtmlBody);
        Assert.DoesNotContain(SecretDetail, call.HtmlBody);
        Assert.DoesNotContain("payslip", call.HtmlBody);

        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Sent, stored.Status);
        Assert.NotNull(stored.SentAt);
        Assert.Equal(1, stored.AttemptCount);
    }

    [Fact]
    public async Task Delivery_Already_Sent_Is_A_No_Op()
    {
        await using var db = BuildContext();
        var (alertId, _, _) = await SeedAlertWithDelivery(db);
        var seeded = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.True(seeded.Claim(Guid.NewGuid(), new DateTimeOffset(FixedUtcNow)).IsSuccess);
        seeded.MarkSent(new DateTimeOffset(FixedUtcNow));
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var job = BuildJob(db, emailSender);

        await job.SendAsync(alertId);

        Assert.Empty(emailSender.Calls);
        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Equal(EmailDeliveryStatus.Sent, stored.Status);
    }

    [Fact]
    public async Task Missing_Delivery_Row_Returns_Without_Sending()
    {
        await using var db = BuildContext();
        var emailSender = new FakeEmailSender();
        var job = BuildJob(db, emailSender);

        await job.SendAsync(Guid.NewGuid());

        Assert.Empty(emailSender.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task No_Internal_Recipient_Configured_Marks_Delivery_Skipped_Without_Sending(string? recipient)
    {
        await using var db = BuildContext();
        var (alertId, _, _) = await SeedAlertWithDelivery(db);
        var emailSender = new FakeEmailSender();
        var job = BuildJob(db, emailSender, recipient: recipient);

        await job.SendAsync(alertId);

        Assert.Empty(emailSender.Calls);
        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Skipped, stored.Status);
        Assert.Equal(0, stored.AttemptCount);
    }

    [Fact]
    public async Task Alert_Missing_Marks_Delivery_Failed_With_Alert_No_Longer_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var alertId = Guid.NewGuid();
        db.OperationalAlertEmailDeliveries.Add(
            OperationalAlertEmailDelivery.Create(Guid.NewGuid(), alertId, companyId, new DateTimeOffset(FixedUtcNow)));
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var job = BuildJob(db, emailSender);

        await job.SendAsync(alertId);

        Assert.Empty(emailSender.Calls);
        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Failed, stored.Status);
        Assert.Equal("Alert no longer exists.", stored.FailureReason);
    }

    [Fact]
    public async Task Transient_Failure_With_Null_Context_Leaves_Delivery_Pending_And_Rethrows()
    {
        await using var db = BuildContext();
        var (alertId, _, _) = await SeedAlertWithDelivery(db);
        var emailSender = new FakeEmailSender(failuresBeforeSuccess: int.MaxValue);
        var job = BuildJob(db, emailSender);

        await Assert.ThrowsAsync<HttpRequestException>(() => job.SendAsync(alertId));

        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Pending, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
        Assert.NotNull(stored.LastAttemptAt);
        Assert.Null(stored.SentAt);
        Assert.Null(stored.FailureReason);
    }

    [Fact]
    public async Task Transient_Failure_On_Provider_HttpRequestException_Rethrows()
    {
        await using var db = BuildContext();
        var (alertId, _, _) = await SeedAlertWithDelivery(db);
        var emailSender = new FakeEmailSender(
            failuresBeforeSuccess: int.MaxValue,
            exceptionFactory: () => new HttpRequestException("provider down"));
        var job = BuildJob(db, emailSender);

        await Assert.ThrowsAsync<HttpRequestException>(() => job.SendAsync(alertId));

        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.NotEqual(EmailDeliveryStatus.Sent, stored.Status);
    }

    [Fact]
    public async Task Two_Sequential_Sends_Only_Deliver_Once()
    {
        await using var db = BuildContext();
        var (alertId, _, _) = await SeedAlertWithDelivery(db);
        var emailSender = new FakeEmailSender();
        var job = BuildJob(db, emailSender);

        await job.SendAsync(alertId);
        await job.SendAsync(alertId);

        Assert.Single(emailSender.Calls);
        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Sent, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
    }


    [Fact]
    public async Task Send_Fails_Once_Then_Succeeds_On_Retry_Delivers_Exactly_One_Email()
    {
        await using var db = BuildContext();
        var (alertId, _, _) = await SeedAlertWithDelivery(db);
        var emailSender = new FakeEmailSender(failuresBeforeSuccess: 1);
        var job = BuildJob(db, emailSender);

        await Assert.ThrowsAsync<HttpRequestException>(() => job.SendAsync(alertId));

        var afterFailure = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Pending, afterFailure.Status);
        Assert.Equal(1, afterFailure.AttemptCount);
        Assert.Null(afterFailure.LeaseOwnerToken);
        Assert.Null(afterFailure.LeaseExpiresAt);
        Assert.Empty(emailSender.Calls);

        await job.SendAsync(alertId);

        Assert.Single(emailSender.Calls);
        var afterSuccess = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Sent, afterSuccess.Status);
        Assert.Equal(2, afterSuccess.AttemptCount);
        Assert.NotNull(afterSuccess.SentAt);
        Assert.Null(afterSuccess.LeaseExpiresAt);
    }


    [Fact]
    public async Task Last_Attempt_Failure_Marks_Delivery_Failed_With_Sanitized_Reason()
    {
        await using var db = BuildContext();
        var (alertId, _, _) = await SeedAlertWithDelivery(db);
        var seeded = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        ExhaustAttemptsTo(seeded, OperationalAlertEmailDelivery.MaxAttempts - 1);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender(
            failuresBeforeSuccess: int.MaxValue,
            exceptionFactory: () => new HttpRequestException("provider down"));
        var job = BuildJob(db, emailSender);

        await Assert.ThrowsAsync<HttpRequestException>(() => job.SendAsync(alertId));

        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Failed, stored.Status);
        Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, stored.AttemptCount);
        Assert.Equal("Email provider error.", stored.FailureReason);
        Assert.Null(stored.LeaseExpiresAt);
    }

    [Fact]
    public async Task Delivery_With_Exhausted_Attempts_Is_Marked_Failed_Without_Sending()
    {
        await using var db = BuildContext();
        var (alertId, _, _) = await SeedAlertWithDelivery(db);
        var seeded = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        ExhaustAttemptsTo(seeded, OperationalAlertEmailDelivery.MaxAttempts);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var job = BuildJob(db, emailSender);

        await job.SendAsync(alertId);

        Assert.Empty(emailSender.Calls);
        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Failed, stored.Status);
        Assert.Equal("Delivery abandoned after repeated failed attempts.", stored.FailureReason);

        await job.SendAsync(alertId);
        Assert.Empty(emailSender.Calls);
    }


    [Fact]
    public async Task Row_Left_Sending_With_A_Live_Lease_Is_Not_Claimable_And_Does_Not_Send()
    {
        await using var db = BuildContext();
        var (alertId, _, _) = await SeedAlertWithDelivery(db);
        var seeded = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.True(seeded.Claim(Guid.NewGuid(), new DateTimeOffset(FixedUtcNow)).IsSuccess);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var job = BuildJob(db, emailSender);

        await job.SendAsync(alertId);

        Assert.Empty(emailSender.Calls);
        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Sending, stored.Status);
        Assert.Equal(1, stored.AttemptCount);
    }

    // Ticket 3L: ownership precedence beats the attempt budget ----------------------------------

    [Fact]
    public async Task Live_Owner_On_The_Final_Attempt_Is_Left_Completely_Unchanged_Even_With_Budget_Exhausted()
    {
        await using var db = BuildContext();
        var (alertId, _, _) = await SeedAlertWithDelivery(db);
        var seeded = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        ExhaustAttemptsTo(seeded, OperationalAlertEmailDelivery.MaxAttempts - 1);
        Assert.True(seeded.Claim(Guid.NewGuid(), new DateTimeOffset(FixedUtcNow)).IsSuccess);
        Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, seeded.AttemptCount);
        Assert.False(seeded.HasAttemptsRemaining);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var job = BuildJob(db, emailSender);

        await job.SendAsync(alertId);

        Assert.Empty(emailSender.Calls);
        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Sending, stored.Status);
        Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, stored.AttemptCount);
        Assert.Null(stored.FailureReason);
        Assert.NotNull(stored.LeaseExpiresAt);
    }

    [Fact]
    public async Task Sending_With_An_Expired_Lease_And_Exhausted_Attempts_Is_Marked_Failed()
    {
        await using var db = BuildContext();
        var (alertId, _, _) = await SeedAlertWithDelivery(db);
        var seeded = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        ExhaustAttemptsTo(seeded, OperationalAlertEmailDelivery.MaxAttempts - 1);
        Assert.True(seeded.Claim(Guid.NewGuid(), new DateTimeOffset(FixedUtcNow).AddDays(-1)).IsSuccess);
        Assert.Equal(OperationalAlertEmailDelivery.MaxAttempts, seeded.AttemptCount);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var job = BuildJob(db, emailSender);

        await job.SendAsync(alertId);

        Assert.Empty(emailSender.Calls);
        var stored = await db.OperationalAlertEmailDeliveries.SingleAsync(d => d.AlertId == alertId);
        Assert.Equal(EmailDeliveryStatus.Failed, stored.Status);
        Assert.Equal("Delivery abandoned after repeated failed attempts.", stored.FailureReason);
        Assert.Null(stored.LeaseExpiresAt);
    }


    private static void ExhaustAttemptsTo(OperationalAlertEmailDelivery delivery, int attempts)
    {
        var t = new DateTimeOffset(FixedUtcNow).AddDays(-1);
        for (var i = 0; i < attempts; i++)
        {
            Assert.True(delivery.Claim(Guid.NewGuid(), t).IsSuccess);
            delivery.ReleaseForRetry(t);
            t = t.AddMinutes(OperationalAlertEmailDelivery.LeaseMinutes + 1);
        }
    }
}
