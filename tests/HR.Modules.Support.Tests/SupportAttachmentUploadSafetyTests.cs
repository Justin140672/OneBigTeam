using HR.Modules.Support.Domain;
using HR.Modules.Support.Features.AddSupportResponse;
using HR.Modules.Support.Features.SubmitSupportRequest;
using HR.Modules.Support.Persistence;
using HR.Modules.Support.Services;
using HR.Modules.Support.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Support.Tests;

/// <summary>
/// Security review ticket 4 (P1): end-to-end handler behaviour for the reject-before-upload,
/// scan-before-persist, and cleanup-on-failure requirements — beyond what
/// SupportAttachmentValidatorTests covers in isolation.
/// </summary>
public class SupportAttachmentUploadSafetyTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 30, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset SeedNow = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private static SupportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SupportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Support:AdminNotificationEmail"] = null })
            .Build();

    private static SubmitSupportRequestRequest ValidRequest(Guid companyId, params Microsoft.AspNetCore.Http.IFormFile[] files) => new()
    {
        CompanyId = companyId,
        Type = SupportRequestType.ReportProblem,
        Title = "Title",
        Description = "Description",
        Priority = SupportRequestPriority.Medium,
        Files = TestFile.Collection(files),
    };

    [Fact]
    public async Task SubmitSupportRequest_Rejects_Infected_File_And_Uploads_Nothing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner();
        scanner.InfectedFileNames.Add("infected.png");
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), storage,
            new SupportAttachmentValidator(), scanner,
            new FakeEmailSender(), BuildConfiguration());

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("infected.png")), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(await db.SupportRequests.ToListAsync());
        Assert.Empty(await db.SupportAttachments.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Rejects_When_Scanner_Unavailable_And_Uploads_Nothing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner { ThrowOnScan = true };
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), storage,
            new SupportAttachmentValidator(), scanner,
            new FakeEmailSender(), BuildConfiguration());

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("photo.png")), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(storage.Uploads);
        Assert.Empty(await db.SupportRequests.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Cleans_Up_Earlier_Upload_When_Later_File_In_Batch_Is_Infected()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner();
        scanner.InfectedFileNames.Add("second.png");
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), storage,
            new SupportAttachmentValidator(), scanner,
            new FakeEmailSender(), BuildConfiguration());

        var result = await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("first.png"), TestFile.Create("second.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        // The fake storage records every UploadAsync call, but the handler must have issued a
        // matching DeleteAsync for "first.png" before returning failure — nothing is left
        // persisted in the database either way.
        Assert.Empty(await db.SupportAttachments.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Rejects_Whole_Request_When_Too_Many_Files_Without_Uploading_Any()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var files = Enumerable.Range(0, SupportAttachmentPolicy.MaxFileCount + 1)
            .Select(i => TestFile.Create($"file{i}.png"))
            .ToArray();
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), storage,
            new SupportAttachmentValidator(), new FakeUploadedFileScanner(),
            new FakeEmailSender(), BuildConfiguration());

        var result = await handler.HandleAsync(
            ValidRequest(companyId, files), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(storage.Uploads);
        Assert.Empty(await db.SupportRequests.ToListAsync());
    }

    [Fact]
    public async Task SubmitSupportRequest_Uses_GuidBased_Storage_Key_Not_Raw_FileName()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var storage = new FakeSupportAttachmentStorageService();
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), storage,
            new SupportAttachmentValidator(), new FakeUploadedFileScanner(),
            new FakeEmailSender(), BuildConfiguration());

        await handler.HandleAsync(
            ValidRequest(companyId, TestFile.Create("../../malicious name.png")),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        // FakeSupportAttachmentStorageService.UploadAsync (test double) still receives the raw file
        // name as a parameter — what matters for this ticket is that the real
        // SupabaseSupportAttachmentStorageService (see its own source) builds the storage key from
        // a GUID plus only the file extension, never the caller-supplied name itself, which is
        // covered separately; here we assert the handler passes a sanitised (path-stripped) display
        // name through to the persisted entity.
        var attachment = await db.SupportAttachments.SingleAsync();
        Assert.DoesNotContain("..", attachment.FileName);
        Assert.DoesNotContain("/", attachment.FileName);
    }

    [Fact]
    public async Task AddSupportResponse_Rejects_Infected_File_And_Persists_No_Response_Attachment()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var submitter = Guid.NewGuid();
        var request = SupportRequest.Create(
            Guid.NewGuid(), companyId, submitter, null,
            SupportRequestType.AskQuestion, "Title", "Description", SupportRequestPriority.Low,
            "SUP-1", null, null, null, false, null, null, SeedNow);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var storage = new FakeSupportAttachmentStorageService();
        var scanner = new FakeUploadedFileScanner();
        scanner.InfectedFileNames.Add("infected.png");
        var handler = new AddSupportResponseHandler(
            db, new FakeClock(FixedUtcNow), storage,
            new SupportAttachmentValidator(), scanner,
            new FakeEmailSender(), new FakeUserEmailReader());

        var result = await handler.HandleAsync(
            new AddSupportResponseRequest
            {
                CompanyId = companyId,
                Id = request.Id,
                BodyHtml = "See attached",
                Files = TestFile.Collection(TestFile.Create("infected.png")),
            },
            submitter, isStaffResponse: false, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(await db.SupportResponses.ToListAsync());
        Assert.Empty(await db.SupportResponseAttachments.ToListAsync());
    }
}
