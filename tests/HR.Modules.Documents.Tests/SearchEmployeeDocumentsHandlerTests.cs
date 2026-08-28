using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.SearchEmployeeDocuments;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class SearchEmployeeDocumentsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    // ── Company scoping ────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Excludes_Documents_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var typeA = await SeedType(db, companyA);
        var typeB = await SeedType(db, companyB);

        await SeedDoc(db, companyA, typeA.Id, "A Doc", "a.pdf", Guid.NewGuid(), Now);
        await SeedDoc(db, companyB, typeB.Id, "B Doc", "b.pdf", Guid.NewGuid(), Now);

        var result = await Handler(db).HandleAsync(
            Request(companyA), allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("A Doc", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Non_Latest_Versions()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);
        var employeeId = Guid.NewGuid();

        var (old, _) = await SeedDoc(db, companyId, type.Id, "Passport", "passport.pdf", employeeId, Now);
        old.SupersedeAsPreviousVersion(Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            Request(companyId), allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    // ── Access scope ───────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Null_AllowedEmployeeIds_Returns_All_Company_Documents()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        await SeedDoc(db, companyId, type.Id, "Doc 1", "one.pdf", Guid.NewGuid(), Now);
        await SeedDoc(db, companyId, type.Id, "Doc 2", "two.pdf", Guid.NewGuid(), Now);

        var result = await Handler(db).HandleAsync(
            Request(companyId), allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Equal(2, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Restricted_AllowedEmployeeIds_Filters_Out_Employees()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);
        var allowed = Guid.NewGuid();
        var notAllowed = Guid.NewGuid();

        await SeedDoc(db, companyId, type.Id, "Allowed Doc", "allowed.pdf", allowed, Now);
        await SeedDoc(db, companyId, type.Id, "Not Allowed Doc", "notallowed.pdf", notAllowed, Now);

        var result = await Handler(db).HandleAsync(
            Request(companyId), allowedEmployeeIds: [allowed], callerIsHrAdministrator: false, CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Allowed Doc", result.Value.Items[0].Title);
    }

    // ── Archived exclusion ─────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Excludes_Archived_By_Default()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        var (ed, _) = await SeedDoc(db, companyId, type.Id, "Archived Doc", "archived.pdf", Guid.NewGuid(), Now);
        ed.Archive(Guid.NewGuid(), "no longer needed", Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { IncludeArchived = false },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Includes_Archived_When_Requested_And_Caller_Is_HrAdministrator()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        var (ed, _) = await SeedDoc(db, companyId, type.Id, "Archived Doc", "archived.pdf", Guid.NewGuid(), Now);
        ed.Archive(Guid.NewGuid(), "no longer needed", Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { IncludeArchived = true },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_IncludeArchived_Is_Ignored_For_NonHrAdministrator()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);
        var employeeId = Guid.NewGuid();

        var (ed, _) = await SeedDoc(db, companyId, type.Id, "Archived Doc", "archived.pdf", employeeId, Now);
        ed.Archive(Guid.NewGuid(), "no longer needed", Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { IncludeArchived = true },
            allowedEmployeeIds: [employeeId], callerIsHrAdministrator: false, CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_IncludeArchived_False_Still_Excludes_Archived_For_HrAdministrator()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        var (ed, _) = await SeedDoc(db, companyId, type.Id, "Archived Doc", "archived.pdf", Guid.NewGuid(), Now);
        ed.Archive(Guid.NewGuid(), "no longer needed", Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { IncludeArchived = false },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    // ── SearchText ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_SearchText_Matches_Title_Case_Insensitively()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        await SeedDoc(db, companyId, type.Id, "Right To Work Certificate", "rtw.pdf", Guid.NewGuid(), Now);
        await SeedDoc(db, companyId, type.Id, "Passport", "passport.pdf", Guid.NewGuid(), Now);

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { SearchText = "right to WORK" },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Right To Work Certificate", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_SearchText_Matches_FileName_Case_Insensitively()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        await SeedDoc(db, companyId, type.Id, "Certificate", "SpecialFile.PDF", Guid.NewGuid(), Now);
        await SeedDoc(db, companyId, type.Id, "Other", "other.pdf", Guid.NewGuid(), Now);

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { SearchText = "specialfile" },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Certificate", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_SearchText_No_Match_Returns_Empty()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        await SeedDoc(db, companyId, type.Id, "Passport", "passport.pdf", Guid.NewGuid(), Now);

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { SearchText = "nonexistent" },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    // ── Exact filters ──────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Filters_By_DocumentTypeId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var typeA = await SeedType(db, companyId, "Passport");
        var typeB = await SeedType(db, companyId, "Contract");

        await SeedDoc(db, companyId, typeA.Id, "Doc A", "a.pdf", Guid.NewGuid(), Now);
        await SeedDoc(db, companyId, typeB.Id, "Doc B", "b.pdf", Guid.NewGuid(), Now);

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { DocumentTypeId = typeA.Id },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Doc A", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_EmployeeId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();

        await SeedDoc(db, companyId, type.Id, "Doc A", "a.pdf", employeeA, Now);
        await SeedDoc(db, companyId, type.Id, "Doc B", "b.pdf", employeeB, Now);

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { EmployeeId = employeeA },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal(employeeA, result.Value.Items[0].EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        var (_, expiredDoc) = await SeedDoc(db, companyId, type.Id, "Expired Doc", "expired.pdf", Guid.NewGuid(), Now);
        expiredDoc.Expire(Now);
        await SeedDoc(db, companyId, type.Id, "Active Doc", "active.pdf", Guid.NewGuid(), Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { Status = DocumentStatus.Expired },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Expired Doc", result.Value.Items[0].Title);
    }

    // ── Uploaded date range (CreatedAt) ────────────────────────────────────

    [Fact]
    public async Task HandleAsync_UploadedFrom_Excludes_Earlier_CreatedAt()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        await SeedDoc(db, companyId, type.Id, "Earlier", "earlier.pdf", Guid.NewGuid(), Now.AddDays(-5));
        await SeedDoc(db, companyId, type.Id, "OnBoundary", "onboundary.pdf", Guid.NewGuid(), Now);

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { UploadedFrom = DateOnly.FromDateTime(Now.Date) },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("OnBoundary", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_UploadedTo_Includes_Documents_Created_On_Boundary_Date()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        await SeedDoc(db, companyId, type.Id, "OnBoundary", "onboundary.pdf", Guid.NewGuid(), Now);
        await SeedDoc(db, companyId, type.Id, "Later", "later.pdf", Guid.NewGuid(), Now.AddDays(5));

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { UploadedTo = DateOnly.FromDateTime(Now.Date) },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("OnBoundary", result.Value.Items[0].Title);
    }

    // ── Expiry date range ──────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ExpiresFrom_Excludes_Documents_With_No_ExpiryDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        await SeedDoc(db, companyId, type.Id, "No Expiry", "noexpiry.pdf", Guid.NewGuid(), Now, expiryDate: null);

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { ExpiresFrom = new DateOnly(2026, 1, 1) },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_ExpiresFrom_And_ExpiresTo_Include_Boundary_Dates()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);
        var from = new DateOnly(2026, 6, 1);
        var to = new DateOnly(2026, 6, 30);

        await SeedDoc(db, companyId, type.Id, "OnStart", "onstart.pdf", Guid.NewGuid(), Now, expiryDate: from);
        await SeedDoc(db, companyId, type.Id, "OnEnd", "onend.pdf", Guid.NewGuid(), Now, expiryDate: to);
        await SeedDoc(db, companyId, type.Id, "BeforeStart", "before.pdf", Guid.NewGuid(), Now, expiryDate: from.AddDays(-1));
        await SeedDoc(db, companyId, type.Id, "AfterEnd", "after.pdf", Guid.NewGuid(), Now, expiryDate: to.AddDays(1));

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { ExpiresFrom = from, ExpiresTo = to },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Contains(result.Value.Items, i => i.Title == "OnStart");
        Assert.Contains(result.Value.Items, i => i.Title == "OnEnd");
    }

    // ── Pagination and ordering ────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Paginates_Results_And_Reports_TotalCount_And_TotalPages()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        for (var i = 0; i < 5; i++)
            await SeedDoc(db, companyId, type.Id, $"Doc {i}", $"doc{i}.pdf", Guid.NewGuid(), Now.AddMinutes(i));

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { PageNumber = 2, PageSize = 2 },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal(5, result.Value.TotalCount);
        Assert.Equal(2, result.Value.PageNumber);
        Assert.Equal(2, result.Value.PageSize);
        Assert.Equal(3, result.Value.TotalPages);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_CreatedAt_Descending_Then_Id_Ascending()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);

        // Two documents share the exact same CreatedAt to exercise the Id tiebreaker.
        var idLow = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var idHigh = Guid.Parse("00000000-0000-0000-0000-000000000002");

        await SeedDoc(db, companyId, type.Id, "Oldest", "oldest.pdf", Guid.NewGuid(), Now.AddMinutes(-10));
        await SeedDoc(db, companyId, type.Id, "TieHigh", "tiehigh.pdf", Guid.NewGuid(), Now, employeeDocumentId: idHigh);
        await SeedDoc(db, companyId, type.Id, "TieLow", "tielow.pdf", Guid.NewGuid(), Now, employeeDocumentId: idLow);

        var result = await Handler(db).HandleAsync(
            Request(companyId), allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Equal(["TieLow", "TieHigh", "Oldest"], result.Value!.Items.Select(i => i.Title));
    }

    // ── Employee name resolution ───────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_Resolves_Employee_Name_From_Reader()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);
        var employeeId = Guid.NewGuid();

        await SeedDoc(db, companyId, type.Id, "Doc", "doc.pdf", employeeId, Now);

        var names = new Dictionary<Guid, string> { [employeeId] = "Priya Shah" };
        var result = await Handler(db, names).HandleAsync(
            Request(companyId), allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Equal("Priya Shah", result.Value!.Items[0].EmployeeName);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Guid_String_When_Name_Not_Found()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var type = await SeedType(db, companyId);
        var employeeId = Guid.NewGuid();

        await SeedDoc(db, companyId, type.Id, "Doc", "doc.pdf", employeeId, Now);

        var result = await Handler(db, new Dictionary<Guid, string>()).HandleAsync(
            Request(companyId), allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Equal(employeeId.ToString(), result.Value!.Items[0].EmployeeName);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_UploadedBy()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var type       = await SeedType(db, companyId);
        var employeeId = Guid.NewGuid();
        var uploaderA  = Guid.NewGuid();
        var uploaderB  = Guid.NewGuid();

        // SeedDoc uses Guid.NewGuid() for uploadedBy internally; we seed manually to control uploader.
        var docA = Document.Create(Guid.NewGuid(), companyId, employeeId, "DocA", null, type.Id, "a.pdf", 100, "application/pdf", "key/a.pdf", null, uploaderA, Now);
        var edA  = EmployeeDocument.Create(Guid.NewGuid(), companyId, employeeId, docA.Id, Guid.NewGuid(), Now);
        var docB = Document.Create(Guid.NewGuid(), companyId, employeeId, "DocB", null, type.Id, "b.pdf", 100, "application/pdf", "key/b.pdf", null, uploaderB, Now);
        var edB  = EmployeeDocument.Create(Guid.NewGuid(), companyId, employeeId, docB.Id, Guid.NewGuid(), Now);
        db.Documents.AddRange(docA, docB);
        db.EmployeeDocuments.AddRange(edA, edB);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            Request(companyId) with { UploadedBy = uploaderA },
            allowedEmployeeIds: null, callerIsHrAdministrator: true, CancellationToken.None);

        Assert.Equal(1, result.Value!.TotalCount);
        Assert.Equal("DocA", result.Value.Items[0].Title);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static SearchEmployeeDocumentsHandler Handler(
        DocumentsDbContext db, Dictionary<Guid, string>? names = null) =>
        new(db, new FakeEmployeeNameReader(names));

    private static SearchEmployeeDocumentsRequest Request(Guid companyId) => new()
    {
        CompanyId = companyId,
        PageNumber = 1,
        PageSize = 20,
    };

    private static async Task<DocumentType> SeedType(DocumentsDbContext db, Guid companyId, string name = "Passport")
    {
        var type = DocumentType.Create(Guid.NewGuid(), companyId, name, null, Now);
        db.DocumentTypes.Add(type);
        await db.SaveChangesAsync();
        return type;
    }

    private static async Task<(EmployeeDocument EmployeeDocument, Document Document)> SeedDoc(
        DocumentsDbContext db,
        Guid companyId,
        Guid documentTypeId,
        string title,
        string fileName,
        Guid employeeId,
        DateTimeOffset createdAt,
        DateOnly? expiryDate = null,
        Guid? employeeDocumentId = null)
    {
        var document = Document.Create(
            Guid.NewGuid(), companyId, employeeId, title, null, documentTypeId, fileName, 100,
            "application/pdf", $"key/{fileName}", null, Guid.NewGuid(), createdAt);
        db.Documents.Add(document);

        var employeeDocument = EmployeeDocument.Create(
            employeeDocumentId ?? Guid.NewGuid(), companyId, employeeId, document.Id, Guid.NewGuid(), createdAt,
            expiryDate: expiryDate);
        db.EmployeeDocuments.Add(employeeDocument);

        await db.SaveChangesAsync();
        return (employeeDocument, document);
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
