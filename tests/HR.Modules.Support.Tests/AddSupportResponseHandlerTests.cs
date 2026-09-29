using HR.Modules.Support.Domain;
using HR.Modules.Support.Features.AddSupportResponse;
using HR.Modules.Support.Persistence;
using HR.Modules.Support.Services;
using HR.Modules.Support.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Support.Tests;

public class AddSupportResponseHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset SeedNow = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private static SupportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SupportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static IServiceScopeFactory BuildScopeFactory() =>
        new ServiceCollection()
            .AddDbContext<SupportDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")))
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private static SupportRequest CreateRequest(Guid companyId, Guid submittedByUserId) =>
        SupportRequest.Create(
            Guid.NewGuid(), companyId, submittedByUserId, null,
            SupportRequestType.AskQuestion, "Title", "Description", SupportRequestPriority.Low,
            "SUP-1", null, null, null, false, null, null, SeedNow);

    private static AddSupportResponseHandler BuildHandler(
        SupportDbContext db,
        FakeSupportAttachmentStorageService? storage = null,
        FakeEmailSender? emailSender = null,
        FakeUserEmailReader? userEmailReader = null,
        FakeUploadedFileScanner? fileScanner = null) =>
        new(db, new FakeClock(FixedUtcNow), storage ?? new FakeSupportAttachmentStorageService(),
            new SupportAttachmentValidator(), fileScanner ?? new FakeUploadedFileScanner(),
            emailSender ?? new FakeEmailSender(), userEmailReader ?? new FakeUserEmailReader(),
            Infrastructure.TestExecutionContext.Accessor, BuildScopeFactory(), NullLogger<AddSupportResponseHandler>.Instance);

    [Fact]
    public async Task HandleAsync_Flags_Customer_Response_As_Not_Staff()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var request = CreateRequest(companyId, submitter);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new AddSupportResponseRequest { CompanyId = companyId, Id = request.Id, BodyHtml = "Customer reply" },
            submitter, isStaffResponse: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsStaffResponse);

        var saved = await db.SupportResponses.SingleAsync();
        Assert.False(saved.IsStaffResponse);
        Assert.Equal(submitter, saved.AuthorUserId);
    }

    [Fact]
    public async Task HandleAsync_Flags_Staff_Response_As_Staff()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var request = CreateRequest(companyId, submitter);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var staffUserId = Guid.NewGuid();
        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new AddSupportResponseRequest { CompanyId = companyId, Id = request.Id, BodyHtml = "Staff reply" },
            staffUserId, isStaffResponse: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsStaffResponse);

        var saved = await db.SupportResponses.SingleAsync();
        Assert.True(saved.IsStaffResponse);
    }

    [Fact]
    public async Task HandleAsync_Staff_Reply_Triggers_NotificationAttempt()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var request = CreateRequest(companyId, submitter);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var handler = BuildHandler(db, emailSender: emailSender, userEmailReader: new FakeUserEmailReader("customer@example.test"));

        var result = await handler.HandleAsync(
            new AddSupportResponseRequest { CompanyId = companyId, Id = request.Id, BodyHtml = "Staff reply" },
            Guid.NewGuid(), isStaffResponse: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var attempt = await db.SupportNotificationAttempts.SingleAsync(a => a.SupportRequestId == request.Id);
        Assert.Equal(SupportNotificationType.StaffReplyCustomerNotification, attempt.NotificationType);
        Assert.Equal(SupportNotificationStatus.Sent, attempt.Status);
        Assert.Equal("customer@example.test", attempt.RecipientEmail);
        Assert.Single(emailSender.Sent);
    }

    [Fact]
    public async Task HandleAsync_NonStaff_Reply_Does_Not_Trigger_NotificationAttempt()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var request = CreateRequest(companyId, submitter);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var handler = BuildHandler(db, emailSender: emailSender);

        var result = await handler.HandleAsync(
            new AddSupportResponseRequest { CompanyId = companyId, Id = request.Id, BodyHtml = "Customer reply" },
            submitter, isStaffResponse: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await db.SupportNotificationAttempts.ToListAsync());
        Assert.Empty(emailSender.Sent);
    }

    [Fact]
    public async Task HandleAsync_Staff_Reply_Records_Failed_NotificationAttempt_When_Recipient_Email_Missing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var request = CreateRequest(companyId, submitter);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db, userEmailReader: new FakeUserEmailReader(null));

        var result = await handler.HandleAsync(
            new AddSupportResponseRequest { CompanyId = companyId, Id = request.Id, BodyHtml = "Staff reply" },
            Guid.NewGuid(), isStaffResponse: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var attempt = await db.SupportNotificationAttempts.SingleAsync(a => a.SupportRequestId == request.Id);
        Assert.Equal(SupportNotificationStatus.Failed, attempt.Status);
    }

    [Fact]
    public async Task HandleAsync_Persists_Attached_Files()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var request = CreateRequest(companyId, submitter);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var storage = new FakeSupportAttachmentStorageService();
        var handler = BuildHandler(db, storage: storage);

        var addRequest = new AddSupportResponseRequest
        {
            CompanyId = companyId,
            Id = request.Id,
            BodyHtml = "See attached",
            Files = TestFile.Collection(TestFile.Create("evidence.png")),
        };

        var result = await handler.HandleAsync(addRequest, submitter, isStaffResponse: false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(storage.Uploads);
        var attachments = await db.SupportResponseAttachments.Where(a => a.SupportResponseId == result.Value!.Id).ToListAsync();
        Assert.Single(attachments);
        Assert.Equal("evidence.png", attachments[0].FileName);
    }

    [Fact]
    public async Task HandleAsync_Touches_SupportRequest_UpdatedAt()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var request = CreateRequest(companyId, submitter);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        await handler.HandleAsync(
            new AddSupportResponseRequest { CompanyId = companyId, Id = request.Id, BodyHtml = "Reply" },
            submitter, isStaffResponse: false, CancellationToken.None);

        var saved = await db.SupportRequests.SingleAsync(r => r.Id == request.Id);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), saved.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            new AddSupportResponseRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid(), BodyHtml = "Reply" },
            Guid.NewGuid(), isStaffResponse: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var request = CreateRequest(Guid.NewGuid(), Guid.NewGuid());
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new AddSupportResponseRequest { CompanyId = Guid.NewGuid(), Id = request.Id, BodyHtml = "Reply" },
            Guid.NewGuid(), isStaffResponse: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }


    [Fact]
    public async Task HandleAsync_Persists_Sanitised_Body_For_Malicious_Input()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var request = CreateRequest(companyId, submitter);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        const string malicious =
            "<p>Hi <strong>there</strong></p><script>alert(1)</script><img src=x onerror=alert(1)><a href=\"javascript:alert(1)\">x</a>";

        var handler = BuildHandler(db);
        var result = await handler.HandleAsync(
            new AddSupportResponseRequest { CompanyId = companyId, Id = request.Id, BodyHtml = malicious },
            submitter, isStaffResponse: false, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var saved = await db.SupportResponses.AsNoTracking().SingleAsync(r => r.Id == result.Value!.Id);
        Assert.DoesNotContain("<script", saved.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", saved.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", saved.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", saved.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<strong>there</strong>", saved.BodyHtml);
        Assert.Equal(HR.SharedKernel.Html.SupportHtmlSanitizer.Sanitize(malicious), saved.BodyHtml);
    }

    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<img src=x onerror=alert(1)><iframe src=\"javascript:alert(1)\"></iframe><svg onload=alert(1)></svg>")]
    [InlineData("<script></script>")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<ScRiPt>fetch('https://evil.example')</sCrIpT><style>p{}</style>")]
    [InlineData("<img src=x onerror=alert(1)>   <iframe src=\"https://evil.example\"></iframe>")]
    public async Task HandleAsync_Returns_Validation_And_Persists_Nothing_When_Body_Has_No_Permitted_Content(string body)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var request = CreateRequest(companyId, submitter);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();
        var updatedAtBefore = request.UpdatedAt;

        var emailSender = new FakeEmailSender();
        var handler = BuildHandler(db, emailSender: emailSender, userEmailReader: new FakeUserEmailReader("customer@example.test"));
        var result = await handler.HandleAsync(
            new AddSupportResponseRequest { CompanyId = companyId, Id = request.Id, BodyHtml = body },
            Guid.NewGuid(), isStaffResponse: true, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(await db.SupportResponses.ToListAsync());
        Assert.Empty(await db.SupportNotificationAttempts.ToListAsync());
        Assert.Empty(emailSender.Sent);

        var reloaded = await db.SupportRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id);
        Assert.Equal(updatedAtBefore, reloaded.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Empty_Sanitised_Body_Before_Uploading_Attachments()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var request = CreateRequest(companyId, submitter);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var storage = new FakeSupportAttachmentStorageService();
        var handler = BuildHandler(db, storage: storage);
        var result = await handler.HandleAsync(
            new AddSupportResponseRequest
            {
                CompanyId = companyId,
                Id = request.Id,
                BodyHtml = "<img src=x onerror=alert(1)>",
                Files = TestFile.Collection(TestFile.Create("evidence.png")),
            },
            submitter, isStaffResponse: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(storage.Uploads);
        Assert.Empty(await db.SupportResponseAttachments.ToListAsync());
    }
}
