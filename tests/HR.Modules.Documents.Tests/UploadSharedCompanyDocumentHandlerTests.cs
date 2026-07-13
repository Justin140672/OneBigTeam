using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.UploadSharedCompanyDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Tests;

public class UploadSharedCompanyDocumentHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 13, 10, 0, 0, DateTimeKind.Utc);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static UploadSharedCompanyDocumentHandler BuildHandler(
        DocumentsDbContext db,
        FakeDocumentStorageService? storage = null,
        FakeVirusScanService? scanner = null,
        FileUploadOptions? options = null,
        FakeEmployeeAudienceReader? audienceReader = null) =>
        new(db,
            storage ?? new FakeDocumentStorageService(),
            new FileUploadValidator(Options.Create(options ?? new FileUploadOptions())),
            scanner ?? new FakeVirusScanService(),
            new SharedCompanyDocumentAudienceRuleBuilder(audienceReader ?? new FakeEmployeeAudienceReader()),
            new FakeClock(FixedUtcNow));

    private static async Task<CompanyDocumentCategory> SeedCategory(
        DocumentsDbContext db, Guid companyId, string name = "Policy")
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, name, DateTimeOffset.UtcNow);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    // Produces a PDF file with valid magic bytes so magic-byte validation passes.
    private static IFormFile FakePdfFile(string fileName = "policy.pdf", int extraSize = 1020) =>
        FakeFile(fileName, "application/pdf", PdfBytes(extraSize));

    private static IFormFile FakeFile(string fileName, string contentType, byte[] content) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "File", fileName)
        {
            Headers     = new HeaderDictionary(),
            ContentType = contentType,
        };

    // %PDF- followed by padding
    private static byte[] PdfBytes(int extraSize = 1020)
    {
        var magic = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D }; // %PDF-
        var bytes = new byte[magic.Length + extraSize];
        magic.CopyTo(bytes, 0);
        return bytes;
    }

    private static UploadSharedCompanyDocumentRequest BuildRequest(
        Guid companyId,
        Guid categoryId,
        IFormFile? file        = null,
        string title           = "Remote Working Policy",
        string? description    = null,
        DateOnly? effectiveDate = null,
        DateOnly? reviewDate    = null,
        Guid[]? audienceDepartmentIds = null,
        Guid[]? audienceLocationIds   = null,
        Guid[]? audiencePositionProfileIds = null,
        Guid[]? audienceEmployeeIds = null,
        bool requiresAcknowledgement = false,
        DateOnly? acknowledgementDueDate = null,
        string? acknowledgementStatement = null) =>
        new()
        {
            CompanyId                  = companyId,
            CategoryId                 = categoryId,
            Title                      = title,
            Description                = description,
            EffectiveDate              = effectiveDate,
            ReviewDate                 = reviewDate,
            AudienceDepartmentIds      = audienceDepartmentIds ?? [],
            AudienceLocationIds        = audienceLocationIds ?? [],
            AudiencePositionProfileIds = audiencePositionProfileIds ?? [],
            AudienceEmployeeIds        = audienceEmployeeIds ?? [],
            RequiresAcknowledgement    = requiresAcknowledgement,
            AcknowledgementDueDate     = acknowledgementDueDate,
            AcknowledgementStatement   = acknowledgementStatement,
            File                       = file ?? FakePdfFile(),
        };

    [Fact]
    public async Task HandleAsync_Creates_Document_As_Draft()
    {
        await using var db = BuildContext();
        var storage        = new FakeDocumentStorageService();
        var companyId      = Guid.NewGuid();
        var uploadedBy     = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db, storage);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, effectiveDate: new DateOnly(2027, 1, 1), reviewDate: new DateOnly(2028, 1, 1)),
            uploadedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(companyId,                result.Value!.CompanyId);
        Assert.Equal(category.Id,              result.Value.CategoryId);
        Assert.Equal("Remote Working Policy",  result.Value.Title);
        Assert.Equal("policy.pdf",             result.Value.FileName);
        Assert.Equal(1,                        result.Value.VersionNumber);
        Assert.Equal("Draft",                  result.Value.Status);
        Assert.Equal(new DateOnly(2027, 1, 1), result.Value.EffectiveDate);
        Assert.Equal(new DateOnly(2028, 1, 1), result.Value.ReviewDate);
        Assert.Equal(uploadedBy,               result.Value.CreatedBy);

        var saved = await db.SharedCompanyDocuments.SingleAsync();
        Assert.Equal(SharedCompanyDocumentStatus.Draft, saved.Status);
        Assert.Equal(result.Value.Id, saved.Id);

        Assert.Single(storage.Uploads);
        Assert.Equal("policy.pdf", storage.Uploads[0].FileName);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), Guid.NewGuid()),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Is_Inactive()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var stored          = await db.CompanyDocumentCategories.SingleAsync();
        stored.Deactivate(DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        var handler = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Category_Belongs_To_Different_Company_Even_If_Caller_Has_Manage_Rights()
    {
        // This is the "company ownership" tenant-isolation check: a category from company A
        // must never be usable for a document being created under company B.
        await using var db = BuildContext();
        var companyA        = Guid.NewGuid();
        var category         = await SeedCategory(db, companyA);
        var handler          = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(Guid.NewGuid(), category.Id), // different company in the request
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_File_Too_Large()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db, options: new FileUploadOptions { MaxFileSizeBytes = 100 });

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, file: FakePdfFile(extraSize: 200)),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("size", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Extension_Not_Allowed()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id,
                file: FakeFile("malware.exe", "application/octet-stream", [0x4D, 0x5A, 0x00, 0x00, 0x00])),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains(".exe", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_File_Is_Infected()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var scanner        = new FakeVirusScanService { ReturnInfected = true, ThreatName = "EICAR.Test.File" };
        var handler        = BuildHandler(db, scanner: scanner);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("EICAR.Test.File", result.Error.Message);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Magic_Bytes_Do_Not_Match_ContentType()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db);

        var spoofedFile = FakeFile("legit.pdf", "application/pdf", [0x00, 0x00, 0x00, 0x00, 0x00]);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, file: spoofedFile),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("content does not match", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows\\system32\\config.pdf")]
    public async Task HandleAsync_Sanitizes_Path_Traversal_Attempts_In_FileName(string maliciousName)
    {
        await using var db = BuildContext();
        var storage         = new FakeDocumentStorageService();
        var companyId       = Guid.NewGuid();
        var category        = await SeedCategory(db, companyId);
        var handler         = BuildHandler(db, storage);

        // The declared extension must still be a valid, allowed one (".pdf") for validation
        // to reach the filename-safety check at all.
        var file = FakeFile(maliciousName, "application/pdf", PdfBytes());

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, file: file),
            Guid.NewGuid(),
            CancellationToken.None);

        // Path.GetFileName() strips the directory components, so this either succeeds with a
        // safe, traversal-free name, or is rejected outright — it must never reach storage
        // with the raw traversal sequence intact.
        if (result.IsSuccess)
        {
            Assert.DoesNotContain("..", storage.Uploads[0].FileName);
            Assert.DoesNotContain("/", storage.Uploads[0].FileName);
            Assert.DoesNotContain("\\", storage.Uploads[0].FileName);
        }
        else
        {
            Assert.Equal("validation", result.Error.Code);
        }
    }

    [Fact]
    public async Task HandleAsync_StorageKey_Contains_CompanyId()
    {
        await using var db = BuildContext();
        var storage         = new FakeDocumentStorageService();
        var companyId       = Guid.NewGuid();
        var category        = await SeedCategory(db, companyId);
        var handler          = BuildHandler(db, storage);

        await handler.HandleAsync(
            BuildRequest(companyId, category.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.Contains(companyId.ToString(), storage.Uploads[0].StorageKey);
    }

    [Fact]
    public async Task HandleAsync_Deletes_StorageObject_When_DbSave_Fails()
    {
        var storage    = new FakeDocumentStorageService();
        var companyId  = Guid.NewGuid();
        var uploadedBy = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ThrowingDocumentsDbContext(options);
        var category       = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", DateTimeOffset.UtcNow);
        db.CompanyDocumentCategories.Add(category);
        await db.BaseSaveChangesAsync(); // seed without throwing

        var handler = BuildHandler(db, storage);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            handler.HandleAsync(
                BuildRequest(companyId, category.Id),
                uploadedBy,
                CancellationToken.None));

        Assert.Single(storage.Uploads);
        Assert.Single(storage.Deletions);
        Assert.Equal(storage.Uploads[0].StorageKey, storage.Deletions[0]);
    }

    [Fact]
    public async Task HandleAsync_Trims_Title_And_Description()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, title: "  Remote Working Policy  ", description: "  Some notes  "),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Remote Working Policy", result.Value!.Title);

        var saved = await db.SharedCompanyDocuments.SingleAsync();
        Assert.Equal("Remote Working Policy", saved.Title);
        Assert.Equal("Some notes",            saved.Description);
    }

    [Fact]
    public async Task HandleAsync_Handles_Null_Description_And_Dates()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Description);
        Assert.Null(result.Value.EffectiveDate);
        Assert.Null(result.Value.ReviewDate);
    }

    [Fact]
    public async Task HandleAsync_Writes_A_Version_History_Row_For_The_First_Version()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var uploadedBy     = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id),
            uploadedBy,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var version = await db.SharedCompanyDocumentVersions.SingleAsync();
        Assert.Equal(result.Value!.Id, version.SharedCompanyDocumentId);
        Assert.Equal(1,                version.VersionNumber);
        Assert.Equal("policy.pdf",     version.FileName);
        Assert.Equal(uploadedBy,       version.CreatedBy);
    }

    [Fact]
    public async Task HandleAsync_Accepts_RequiresAcknowledgement_And_DepartmentAudience()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var departmentId   = Guid.NewGuid();
        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.ExistingDepartmentIds.Add(departmentId);
        var handler         = BuildHandler(db, audienceReader: audienceReader);

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, audienceDepartmentIds: [departmentId], requiresAcknowledgement: true),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([departmentId], result.Value!.AudienceDepartmentIds);
        Assert.Empty(result.Value.AudienceLocationIds);
        Assert.Empty(result.Value.AudiencePositionProfileIds);
        Assert.Empty(result.Value.AudienceEmployeeIds);
        Assert.True(result.Value.RequiresAcknowledgement);

        var savedRules = await db.SharedCompanyDocumentAudienceRules.ToListAsync();
        Assert.Single(savedRules);
        Assert.Equal(SharedCompanyDocumentAudienceRuleType.Department, savedRules[0].RuleType);
        Assert.Equal(departmentId, savedRules[0].TargetId);
    }

    [Fact]
    public async Task HandleAsync_Stores_AcknowledgementDueDate_And_Statement_When_Required()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var handler   = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(
                companyId, category.Id,
                requiresAcknowledgement: true,
                acknowledgementDueDate: new DateOnly(2027, 1, 1),
                acknowledgementStatement: "Please confirm you have read the new expenses policy."),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2027, 1, 1), result.Value!.AcknowledgementDueDate);
        Assert.Equal("Please confirm you have read the new expenses policy.", result.Value.AcknowledgementStatement);

        var saved = await db.SharedCompanyDocuments.SingleAsync();
        Assert.Equal(new DateOnly(2027, 1, 1), saved.AcknowledgementDueDate);
        Assert.Equal("Please confirm you have read the new expenses policy.", saved.AcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_Clears_AcknowledgementDueDate_And_Statement_When_Not_Required()
    {
        // Submitting a due date/statement alongside RequiresAcknowledgement=false is a client
        // mistake, not a valid state — the handler must not persist settings that only make
        // sense when acknowledgement is actually required.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var handler   = BuildHandler(db);

        var result = await handler.HandleAsync(
            BuildRequest(
                companyId, category.Id,
                requiresAcknowledgement: false,
                acknowledgementDueDate: new DateOnly(2027, 1, 1),
                acknowledgementStatement: "Some statement"),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.AcknowledgementDueDate);
        Assert.Null(result.Value.AcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_Accepts_Multiple_Departments_Locations_Positions_And_Employees_Together()
    {
        // The audience is OR'd, not exclusive — combining every rule type at once is valid.
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var dept1 = Guid.NewGuid();
        var dept2 = Guid.NewGuid();
        var loc1  = Guid.NewGuid();
        var pos1  = Guid.NewGuid();
        var emp1  = Guid.NewGuid();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.ExistingDepartmentIds.Add(dept1);
        audienceReader.ExistingDepartmentIds.Add(dept2);
        audienceReader.ExistingLocationIds.Add(loc1);
        audienceReader.ExistingPositionProfileIds.Add(pos1);
        audienceReader.ExistingEmployeeIds.Add(emp1);
        var handler = BuildHandler(db, audienceReader: audienceReader);

        var result = await handler.HandleAsync(
            BuildRequest(
                companyId, category.Id,
                audienceDepartmentIds: [dept1, dept2],
                audienceLocationIds: [loc1],
                audiencePositionProfileIds: [pos1],
                audienceEmployeeIds: [emp1]),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.AudienceDepartmentIds.Count);
        Assert.Single(result.Value.AudienceLocationIds);
        Assert.Single(result.Value.AudiencePositionProfileIds);
        Assert.Single(result.Value.AudienceEmployeeIds);

        var savedRules = await db.SharedCompanyDocumentAudienceRules.ToListAsync();
        Assert.Equal(5, savedRules.Count);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_AudienceDepartment_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db, audienceReader: new FakeEmployeeAudienceReader());

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, audienceDepartmentIds: [Guid.NewGuid()]),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_AudiencePosition_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db, audienceReader: new FakeEmployeeAudienceReader());

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, audiencePositionProfileIds: [Guid.NewGuid()]),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_AudienceEmployee_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var companyId      = Guid.NewGuid();
        var category       = await SeedCategory(db, companyId);
        var handler        = BuildHandler(db, audienceReader: new FakeEmployeeAudienceReader());

        var result = await handler.HandleAsync(
            BuildRequest(companyId, category.Id, audienceEmployeeIds: [Guid.NewGuid()]),
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    // Subclass used only in the orphan-cleanup test to simulate a DB save failure.
    private sealed class ThrowingDocumentsDbContext(DbContextOptions<DocumentsDbContext> options)
        : DocumentsDbContext(options)
    {
        public Task<int> BaseSaveChangesAsync(CancellationToken ct = default) =>
            base.SaveChangesAsync(ct);

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Simulated database failure.");
    }
}
