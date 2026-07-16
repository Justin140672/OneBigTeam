using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetPublishedSharedCompanyDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetPublishedSharedCompanyDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Simplified_Detail_For_Published_AllEmployees_Document()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId, "Policy");
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Remote Working Policy", "A description", category.Id,
            "key/p.pdf", "p.pdf", 100, "application/pdf",
            new DateOnly(2026, 1, 1), null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetPublishedSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Remote Working Policy", result.Value!.Title);
        Assert.Equal("Policy",                result.Value.CategoryName);
        Assert.False(result.Value.RequiresAcknowledgement);
        Assert.Null(result.Value.MyAcknowledgedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Draft_Document()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetPublishedSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, Guid.NewGuid(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Caller_Is_Outside_The_Department_Audience()
    {
        await using var db = BuildContext();
        var companyId    = Guid.NewGuid();
        var category     = await SeedCategory(db, companyId);
        var departmentId = Guid.NewGuid();
        var otherDeptId  = Guid.NewGuid();
        var caller        = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Department, departmentId));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.EmployeeAudiences[caller] = new EmployeeAudienceProfile(otherDeptId, null, null);

        var result = await Handler(db, audienceReader).HandleAsync(
            new GetPublishedSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_Not_Forbidden_For_OutOfAudience_Caller()
    {
        // Deliberately can't distinguish "doesn't exist" from "not for you" — this test locks in
        // that non-differentiation so a future change doesn't accidentally leak document
        // existence to callers outside its audience.
        await using var db = BuildContext();
        var companyId    = Guid.NewGuid();
        var category     = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Department, Guid.NewGuid()));
        await db.SaveChangesAsync();

        var trulyMissingResult = await Handler(db).HandleAsync(
            new GetPublishedSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = Guid.NewGuid() }, Guid.NewGuid(),
            CancellationToken.None);
        var outOfAudienceResult = await Handler(db).HandleAsync(
            new GetPublishedSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(trulyMissingResult.Error.Code, outOfAudienceResult.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_My_Acknowledgement_Timestamp_When_Acknowledged()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAcknowledgements.Add(
            SharedCompanyDocumentAcknowledgement.Create(Guid.NewGuid(), companyId, doc.Id, caller, 1, "Statement", null, Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetPublishedSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.True(result.Value!.RequiresAcknowledgement);
        Assert.Equal(Now, result.Value.MyAcknowledgedAt);
    }

    [Fact]
    public async Task HandleAsync_Resolves_Default_Statement_When_HR_Has_Not_Written_A_Custom_One()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2027, 1, 1),
            acknowledgementStatement: null, createdBy: Guid.NewGuid(), now: Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetPublishedSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.Equal("I confirm that I have read and understood this document.", result.Value!.AcknowledgementStatement);
        Assert.Equal(new DateOnly(2027, 1, 1), result.Value.AcknowledgementDueDate);
    }

    [Fact]
    public async Task HandleAsync_Returns_Custom_Statement_When_HR_Has_Written_One()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2027, 1, 1),
            acknowledgementStatement: "I confirm I have read the updated expenses policy.",
            createdBy: Guid.NewGuid(), now: Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetPublishedSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.Equal("I confirm I have read the updated expenses policy.", result.Value!.AcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_MyAcknowledgedAt_Is_Null_When_Another_Employees_Acknowledgement_Exists()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var caller     = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAcknowledgements.Add(
            SharedCompanyDocumentAcknowledgement.Create(Guid.NewGuid(), companyId, doc.Id, someoneElse, 1, "Statement", null, Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetPublishedSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id }, caller,
            CancellationToken.None);

        Assert.Null(result.Value!.MyAcknowledgedAt);
    }

    private static GetPublishedSharedCompanyDocumentHandler Handler(
        DocumentsDbContext db, FakeEmployeeAudienceReader? audienceReader = null) =>
        new(db, new SharedCompanyDocumentAudienceMatcher(db, audienceReader ?? new FakeEmployeeAudienceReader()));

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
