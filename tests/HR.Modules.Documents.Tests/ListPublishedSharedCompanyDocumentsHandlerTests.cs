using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ListPublishedSharedCompanyDocuments;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ListPublishedSharedCompanyDocumentsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Excludes_Draft_And_Archived_Documents()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var draft = CreateDoc(companyId, "Draft Doc", category.Id, "key/d.pdf", "d.pdf", Guid.NewGuid());

        var archived = CreateDoc(companyId, "Archived Doc", category.Id, "key/a.pdf", "a.pdf", Guid.NewGuid());
        archived.Publish(Guid.NewGuid(), Now);
        archived.Archive(Guid.NewGuid(), "Superseded", Now);

        var published = CreateDoc(companyId, "Published Doc", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid());
        published.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(draft, archived, published);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Published Doc", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Includes_AllEmployees_Audience_Documents_For_Any_Caller()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Published Doc", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid());
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Includes_Department_Scoped_Document_For_Caller_In_That_Department()
    {
        await using var db = BuildContext();
        var companyId   = Guid.NewGuid();
        var category    = await SeedCategory(db, companyId);
        var caller       = Guid.NewGuid();
        var departmentId = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Engineering Policy", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid());
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Department, departmentId));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.EmployeeAudiences[caller] = new EmployeeAudienceProfile(departmentId, null, null);

        var result = await Handler(db, audienceReader).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Department_Scoped_Document_For_Caller_In_A_Different_Department()
    {
        await using var db = BuildContext();
        var companyId   = Guid.NewGuid();
        var category    = await SeedCategory(db, companyId);
        var caller       = Guid.NewGuid();
        var engineeringId = Guid.NewGuid();
        var salesId       = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Engineering Policy", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid());
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Department, engineeringId));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.EmployeeAudiences[caller] = new EmployeeAudienceProfile(salesId, null, null);

        var result = await Handler(db, audienceReader).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Includes_Position_Scoped_Document_For_Caller_In_That_Position()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var caller      = Guid.NewGuid();
        var positionId  = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Engineer Policy", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid());
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Position, positionId));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.EmployeeAudiences[caller] = new EmployeeAudienceProfile(null, null, positionId);

        var result = await Handler(db, audienceReader).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Includes_Document_Directly_Naming_The_Caller_As_Selected_Employee()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Just For You", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid());
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Employee, caller));
        await db.SaveChangesAsync();

        // No profile seeded for the caller at all — the employee-id rule must match regardless.
        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_Title()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var b = CreateDoc(companyId, "B Policy", category.Id, "key/b.pdf", "b.pdf", Guid.NewGuid());
        b.Publish(Guid.NewGuid(), Now);
        var a = CreateDoc(companyId, "A Policy", category.Id, "key/a.pdf", "a.pdf", Guid.NewGuid());
        a.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(b, a);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        Assert.Equal(["A Policy", "B Policy"], result.Value!.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task HandleAsync_Excludes_Documents_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyA  = Guid.NewGuid();
        var companyB  = Guid.NewGuid();
        var categoryA = await SeedCategory(db, companyA);
        var categoryB = await SeedCategory(db, companyB);
        var caller    = Guid.NewGuid();

        var docA = CreateDoc(companyA, "A Policy", categoryA.Id, "key/a.pdf", "a.pdf", Guid.NewGuid());
        docA.Publish(Guid.NewGuid(), Now);
        var docB = CreateDoc(companyB, "B Policy", categoryB.Id, "key/b.pdf", "b.pdf", Guid.NewGuid());
        docB.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(docA, docB);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyA }, caller,
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("A Policy", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Maps_Acknowledgement_Requirement_And_Due_Date_From_Document()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var publisher  = Guid.NewGuid();
        var dueDate    = DateOnly.FromDateTime(Now.AddDays(30).DateTime);

        var doc = CreateDoc(companyId, "Ack Policy", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid());
        doc.SetAcknowledgementSettings(true, dueDate, "I confirm that I have read this.", publisher, Now);
        doc.Publish(publisher, Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.True(item.RequiresAcknowledgement);
        Assert.Equal(dueDate, item.AcknowledgementDueDate);
    }

    [Fact]
    public async Task HandleAsync_Sets_MyAcknowledgedAt_When_Caller_Acknowledged_Current_Version()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var publisher  = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Ack Policy", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid());
        doc.Publish(publisher, Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var ackAt = Now.AddHours(1);
        db.SharedCompanyDocumentAcknowledgements.Add(SharedCompanyDocumentAcknowledgement.Create(
            Guid.NewGuid(), companyId, doc.Id, caller, doc.VersionNumber, "I confirm that I have read this.",
            null, ackAt));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(ackAt, item.MyAcknowledgedAt);
    }

    [Fact]
    public async Task HandleAsync_Leaves_MyAcknowledgedAt_Null_When_Caller_Has_Not_Acknowledged()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var publisher  = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Ack Policy", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid());
        doc.Publish(publisher, Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Null(item.MyAcknowledgedAt);
    }

    [Fact]
    public async Task HandleAsync_Ignores_Acknowledgement_Of_A_Superseded_Version()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var publisher  = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Ack Policy", category.Id, "key/p.pdf", "p.pdf", Guid.NewGuid());
        doc.Publish(publisher, Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        // Caller acknowledged version 1; the file is then replaced, bumping the document to
        // version 2 — the stale version-1 acknowledgement must not count towards version 2.
        var staleVersion = doc.VersionNumber;
        db.SharedCompanyDocumentAcknowledgements.Add(SharedCompanyDocumentAcknowledgement.Create(
            Guid.NewGuid(), companyId, doc.Id, caller, staleVersion, "I confirm that I have read this.",
            null, Now));
        doc.ReplaceFile("key/p-v2.pdf", "p-v2.pdf", 200, "application/pdf", publisher, Now);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        var item = Assert.Single(result.Value!.Items);
        Assert.Null(item.MyAcknowledgedAt);
    }

    [Fact]
    public async Task HandleAsync_Orders_By_PublishedAt_Descending_Before_Title()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var yesterday  = Now.AddDays(-1);
        var today      = Now;

        // Titles are chosen so that alphabetical order disagrees with publish-date order — this
        // proves PublishedAt is the primary sort key, not merely a tiebreaker for Title.
        var publishedYesterday = CreateDoc(companyId, "A Policy", category.Id, "key/a.pdf", "a.pdf", Guid.NewGuid());
        publishedYesterday.Publish(Guid.NewGuid(), yesterday);

        var publishedToday = CreateDoc(companyId, "Z Policy", category.Id, "key/z.pdf", "z.pdf", Guid.NewGuid());
        publishedToday.Publish(Guid.NewGuid(), today);

        db.SharedCompanyDocuments.AddRange(publishedYesterday, publishedToday);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListPublishedSharedCompanyDocumentsRequest { CompanyId = companyId }, caller,
            CancellationToken.None);

        Assert.Equal(["Z Policy", "A Policy"], result.Value!.Items.Select(i => i.Title));
    }

    private static ListPublishedSharedCompanyDocumentsHandler Handler(
        DocumentsDbContext db, FakeEmployeeAudienceReader? audienceReader = null) =>
        new(db, audienceReader ?? new FakeEmployeeAudienceReader());

    private static SharedCompanyDocument CreateDoc(
        Guid companyId, string title, Guid categoryId, string storageKey, string fileName, Guid createdBy) =>
        SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, title, null, categoryId, storageKey, fileName, 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, createdBy, Now);

    private static async Task<CompanyDocumentCategory> SeedCategory(
        DocumentsDbContext db, Guid companyId, string name = "Policy")
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, name, Now);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
