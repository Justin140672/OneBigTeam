using HR.Modules.Support.Domain;
using HR.Modules.Support.Features.SubmitSupportRequest;
using HR.Modules.Support.Persistence;
using HR.Modules.Support.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Support.Tests;

public class SubmitSupportRequestHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);

    private static SupportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SupportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static IConfiguration BuildConfiguration(string? adminEmail = "support-admin@example.test") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(adminEmail is null
                ? []
                : new Dictionary<string, string?> { ["Support:AdminNotificationEmail"] = adminEmail })
            .Build();

    private static SubmitSupportRequestRequest ValidRequest(Guid companyId) => new()
    {
        CompanyId = companyId,
        Type = SupportRequestType.ReportProblem,
        Title = "Leave balance not updating",
        Description = "When I approve a leave request the balance doesn't refresh.",
        Priority = SupportRequestPriority.Medium,
        IncludeDiagnostics = true,
        PageUrl = "/leave/requests",
        Browser = "Chrome 128",
        AppVersion = "1.4.2",
        CorrelationId = "corr-123",
    };

    [Fact]
    public async Task HandleAsync_Creates_Request_And_Generates_ReferenceNumber()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), new FakeSupportAttachmentStorageService(),
            new FakeEmailSender(), BuildConfiguration());

        var result = await handler.HandleAsync(ValidRequest(companyId), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.StartsWith("SUP-2026-", result.Value.ReferenceNumber);

        var saved = await db.SupportRequests.SingleAsync();
        Assert.Equal(SupportRequestStatus.Submitted, saved.Status);
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal(result.Value.ReferenceNumber, saved.ReferenceNumber);
    }

    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("\"><script>alert(1)</script>")]
    [InlineData("Tom & Sons <support@example.com>")]
    public async Task HandleAsync_HtmlEncodes_Title_In_Admin_Notification_Email(string maliciousTitle)
    {
        // CodeQL alert: the support request title is user-controlled free text that is interpolated
        // into the admin notification's HTML body — it must be HTML-encoded so it cannot execute as
        // markup/script in the recipient's mail client.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var emailSender = new FakeEmailSender();
        var request = ValidRequest(companyId) with { Title = maliciousTitle };
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), new FakeSupportAttachmentStorageService(),
            emailSender, BuildConfiguration());

        var result = await handler.HandleAsync(request, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(emailSender.Sent);
        Assert.DoesNotContain("<script>", sent.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", sent.HtmlBody, StringComparison.OrdinalIgnoreCase);
        // The encoded form of the dangerous characters must be present instead — "onerror=" may
        // still appear, but only as inert encoded text, never as a live tag/attribute.
        Assert.Contains("&lt;", sent.HtmlBody);
    }

    [Fact]
    public async Task HandleAsync_Builds_AdminLink_Only_For_Http_Or_Https_Configured_BaseUrl()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var emailSender = new FakeEmailSender();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Support:AdminNotificationEmail"] = "support-admin@example.test",
                ["Support:AdminBaseUrl"] = "javascript:alert(1)//",
            })
            .Build();
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), new FakeSupportAttachmentStorageService(),
            emailSender, configuration);

        var result = await handler.HandleAsync(ValidRequest(companyId), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(emailSender.Sent);
        Assert.DoesNotContain("javascript:", sent.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_Generates_Unique_ReferenceNumbers_Across_Multiple_Requests()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), new FakeSupportAttachmentStorageService(),
            new FakeEmailSender(), BuildConfiguration());

        var refs = new HashSet<string>();
        for (var i = 0; i < 10; i++)
        {
            var result = await handler.HandleAsync(ValidRequest(companyId), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
            Assert.True(result.IsSuccess);
            Assert.True(refs.Add(result.Value!.ReferenceNumber), $"Duplicate reference number generated: {result.Value.ReferenceNumber}");
        }
    }

    [Fact]
    public async Task HandleAsync_Captures_Diagnostics_When_IncludeDiagnostics_Is_True()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), new FakeSupportAttachmentStorageService(),
            new FakeEmailSender(), BuildConfiguration());

        var request = ValidRequest(companyId);
        request = request with { IncludeDiagnostics = true };

        var result = await handler.HandleAsync(request, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var saved = await db.SupportRequests.SingleAsync(r => r.Id == result.Value!.Id);
        Assert.True(saved.IncludeDiagnostics);
        Assert.NotNull(saved.DiagnosticsJson);
        Assert.Contains(request.PageUrl!, saved.DiagnosticsJson);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Capture_Diagnostics_When_IncludeDiagnostics_Is_False()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), new FakeSupportAttachmentStorageService(),
            new FakeEmailSender(), BuildConfiguration());

        var request = ValidRequest(companyId);
        request = request with { IncludeDiagnostics = false };

        var result = await handler.HandleAsync(request, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var saved = await db.SupportRequests.SingleAsync(r => r.Id == result.Value!.Id);
        Assert.False(saved.IncludeDiagnostics);
        Assert.Null(saved.DiagnosticsJson);
    }

    [Fact]
    public async Task HandleAsync_Persists_Attached_Files()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), storage, new FakeEmailSender(), BuildConfiguration());

        var request = ValidRequest(companyId);
        request = request with { Files = TestFile.Collection(TestFile.Create("screenshot.png")) };

        var result = await handler.HandleAsync(request, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(storage.Uploads);

        var attachments = await db.SupportAttachments.Where(a => a.SupportRequestId == result.Value!.Id).ToListAsync();
        Assert.Single(attachments);
        Assert.Equal("screenshot.png", attachments[0].FileName);
        Assert.Equal(companyId, attachments[0].CompanyId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Persist_Attachments_When_No_Files_Provided()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), new FakeSupportAttachmentStorageService(),
            new FakeEmailSender(), BuildConfiguration());

        var result = await handler.HandleAsync(ValidRequest(companyId), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(await db.SupportAttachments.Where(a => a.SupportRequestId == result.Value!.Id).ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Records_Sent_NotificationAttempt_When_AdminEmail_Configured_And_Send_Succeeds()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var emailSender = new FakeEmailSender();
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), new FakeSupportAttachmentStorageService(),
            emailSender, BuildConfiguration("admin@example.test"));

        var result = await handler.HandleAsync(ValidRequest(companyId), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        var attempt = await db.SupportNotificationAttempts.SingleAsync(a => a.SupportRequestId == result.Value!.Id);
        Assert.Equal(SupportNotificationType.NewRequestAdminAlert, attempt.NotificationType);
        Assert.Equal(SupportNotificationStatus.Sent, attempt.Status);
        Assert.Equal("admin@example.test", attempt.RecipientEmail);
        Assert.Single(emailSender.Sent);
    }

    [Fact]
    public async Task HandleAsync_Records_Failed_NotificationAttempt_When_AdminEmail_Not_Configured()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), new FakeSupportAttachmentStorageService(),
            new FakeEmailSender(), BuildConfiguration(adminEmail: null));

        var result = await handler.HandleAsync(ValidRequest(companyId), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess); // submission still succeeds even though notification could not be attempted
        var attempt = await db.SupportNotificationAttempts.SingleAsync(a => a.SupportRequestId == result.Value!.Id);
        Assert.Equal(SupportNotificationStatus.Failed, attempt.Status);
        Assert.NotNull(attempt.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_Records_Failed_NotificationAttempt_When_Email_Send_Throws()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var emailSender = new FakeEmailSender { ThrowOnSend = true };
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), new FakeSupportAttachmentStorageService(),
            emailSender, BuildConfiguration("admin@example.test"));

        var result = await handler.HandleAsync(ValidRequest(companyId), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess); // notification failure must not fail the overall submission
        var attempt = await db.SupportNotificationAttempts.SingleAsync(a => a.SupportRequestId == result.Value!.Id);
        Assert.Equal(SupportNotificationStatus.Failed, attempt.Status);
        Assert.Contains("Simulated email provider failure", attempt.ErrorMessage);
    }

    [Fact]
    public async Task HandleAsync_Sets_SubmittedByUserId_And_EmployeeId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), new FakeSupportAttachmentStorageService(),
            new FakeEmailSender(), BuildConfiguration());

        var result = await handler.HandleAsync(ValidRequest(companyId), userId, employeeId, CancellationToken.None);

        var saved = await db.SupportRequests.SingleAsync(r => r.Id == result.Value!.Id);
        Assert.Equal(userId, saved.SubmittedByUserId);
        Assert.Equal(employeeId, saved.SubmittedByEmployeeId);
    }
}
