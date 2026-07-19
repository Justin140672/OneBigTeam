using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetSharedCompanyDocument;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetSharedCompanyDocumentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeEmployeeNameReader(Dictionary<Guid, string>? names = null) : IEmployeeNameReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(names ?? new Dictionary<Guid, string>());
    }

    [Fact]
    public async Task HandleAsync_Returns_Full_Metadata_And_Category_Name()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId, "Policy");
        var createdBy = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Remote Working Policy", "A description", category.Id,
            "key/p.pdf", "p.pdf", 500, "application/pdf",
            new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 1), SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, createdBy, Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var names = new Dictionary<Guid, string> { [createdBy] = "Laura Bennett" };
        var result = await Handler(db, names: names).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Remote Working Policy", result.Value!.Title);
        Assert.Equal("Policy",                result.Value.CategoryName);
        Assert.Equal(1,                       result.Value.VersionNumber);
        Assert.Equal("Draft",                 result.Value.Status);
        Assert.Equal("Laura Bennett",         result.Value.CreatedByName);
        Assert.Equal("Laura Bennett",         result.Value.UpdatedByName);
        Assert.Equal("All Employees",         result.Value.AudienceDescription);
        Assert.Empty(result.Value.AudienceDepartmentIds);
        Assert.Empty(result.Value.AudienceLocationIds);
        Assert.Empty(result.Value.AudiencePositionProfileIds);
        Assert.Empty(result.Value.AudienceEmployeeIds);
        Assert.False(result.Value.RequiresAcknowledgement);
        Assert.Null(result.Value.AcknowledgementDueDate);
        Assert.Null(result.Value.AcknowledgementStatement);
        Assert.Null(result.Value.AcknowledgementProgress);
    }

    [Fact]
    public async Task HandleAsync_Returns_ReviewOwnerEmployeeId_And_Name_When_Set()
    {
        await using var db = BuildContext();
        var companyId     = Guid.NewGuid();
        var category      = await SeedCategory(db, companyId);
        var reviewOwnerId = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, reviewOwnerId, false, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var names = new Dictionary<Guid, string> { [reviewOwnerId] = "Priya Kapoor" };
        var result = await Handler(db, names: names).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(reviewOwnerId,    result.Value!.ReviewOwnerEmployeeId);
        Assert.Equal("Priya Kapoor",   result.Value.ReviewOwnerName);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_ReviewOwner_When_Not_Set()
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
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.ReviewOwnerEmployeeId);
        Assert.Null(result.Value.ReviewOwnerName);
    }

    [Fact]
    public async Task HandleAsync_Returns_Raw_AcknowledgementDueDate_And_Statement()
    {
        // The HR detail view feeds the edit dialog, so it must return the raw stored statement
        // (including null when HR hasn't written one) rather than a resolved default — only the
        // employee-facing GetPublishedSharedCompanyDocument response applies the default fallback.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2027, 1, 1),
            acknowledgementStatement: null, createdBy: Guid.NewGuid(), now: Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.Equal(new DateOnly(2027, 1, 1), result.Value!.AcknowledgementDueDate);
        Assert.Null(result.Value.AcknowledgementStatement);
    }

    [Fact]
    public async Task HandleAsync_Returns_LastReview_Fields_When_A_Review_Has_Been_Completed()
    {
        await using var db = BuildContext();
        var companyId    = Guid.NewGuid();
        var category     = await SeedCategory(db, companyId);
        var reviewedBy   = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, new DateOnly(2026, 1, 1), SharedCompanyDocumentReviewFrequency.Yearly, null, null, false, null, null, Guid.NewGuid(), Now);
        doc.CompleteReview(reviewedBy, "Reviewed against latest legislation.", new DateOnly(2026, 6, 1), new DateOnly(2027, 6, 1), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var names = new Dictionary<Guid, string> { [reviewedBy] = "Nina Reviewer" };
        var result = await Handler(db, names: names).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value!.LastReviewedAt);
        Assert.Equal(reviewedBy, result.Value.LastReviewedByEmployeeId);
        Assert.Equal("Nina Reviewer", result.Value.LastReviewedByName);
        Assert.Equal("Reviewed against latest legislation.", result.Value.LastReviewNotes);
        Assert.Equal(new DateOnly(2027, 6, 1), result.Value.ReviewDate);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_LastReview_Fields_When_No_Review_Has_Been_Completed()
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
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.LastReviewedAt);
        Assert.Null(result.Value.LastReviewedByEmployeeId);
        Assert.Null(result.Value.LastReviewedByName);
        Assert.Null(result.Value.LastReviewNotes);
    }

    [Fact]
    public async Task HandleAsync_Returns_ReviewHistory_Sorted_Newest_First_With_Reviewer_Names()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var reviewer1 = Guid.NewGuid();
        var reviewer2 = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, new DateOnly(2027, 1, 1), SharedCompanyDocumentReviewFrequency.Yearly, null, null, false, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);

        db.SharedCompanyDocumentReviewHistories.AddRange(
            SharedCompanyDocumentReviewHistory.Create(
                Guid.NewGuid(), companyId, doc.Id, new DateOnly(2026, 1, 1), reviewer1, "First review.", null, Now),
            SharedCompanyDocumentReviewHistory.Create(
                Guid.NewGuid(), companyId, doc.Id, new DateOnly(2026, 6, 1), reviewer2, "Second review.", new DateOnly(2026, 1, 1), Now.AddDays(1)));
        await db.SaveChangesAsync();

        var names = new Dictionary<Guid, string> { [reviewer1] = "Nina Reviewer", [reviewer2] = "Omar Reviewer" };
        var result = await Handler(db, names: names).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.ReviewHistory.Count);
        Assert.Equal(new DateOnly(2026, 6, 1), result.Value.ReviewHistory[0].ReviewDate);
        Assert.Equal(reviewer2, result.Value.ReviewHistory[0].ReviewedByEmployeeId);
        Assert.Equal("Omar Reviewer", result.Value.ReviewHistory[0].ReviewedByName);
        Assert.Equal("Second review.", result.Value.ReviewHistory[0].ReviewNotes);
        Assert.Equal(new DateOnly(2026, 1, 1), result.Value.ReviewHistory[0].PreviousReviewDate);

        Assert.Equal(new DateOnly(2026, 1, 1), result.Value.ReviewHistory[1].ReviewDate);
        Assert.Equal(reviewer1, result.Value.ReviewHistory[1].ReviewedByEmployeeId);
        Assert.Equal("Nina Reviewer", result.Value.ReviewHistory[1].ReviewedByName);
        Assert.Equal("First review.", result.Value.ReviewHistory[1].ReviewNotes);
        Assert.Null(result.Value.ReviewHistory[1].PreviousReviewDate);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_ReviewHistory_When_No_Review_Has_Been_Completed()
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
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.ReviewHistory);
        Assert.Empty(result.Value.ReviewHistory);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Document()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var companyA  = Guid.NewGuid();
        var category  = await SeedCategory(db, companyA);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyA, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = Guid.NewGuid(), DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Version_History_Ordered_Newest_First()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        doc.ReplaceFile("key/v2.pdf", "v2.pdf", 200, "application/pdf", Guid.NewGuid(), Now.AddDays(1));
        db.SharedCompanyDocuments.Add(doc);

        db.SharedCompanyDocumentVersions.AddRange(
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 1, "key/v1.pdf", "v1.pdf", 100, "application/pdf", Guid.NewGuid(), Now, versionNote: null, requiresAcknowledgement: false, effectiveDate: null),
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 2, "key/v2.pdf", "v2.pdf", 200, "application/pdf", Guid.NewGuid(), Now.AddDays(1), versionNote: "Updated section 3", requiresAcknowledgement: true, effectiveDate: null));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.Equal(2, result.Value!.VersionHistory.Count);
        Assert.Equal(2, result.Value.VersionHistory[0].VersionNumber);
        Assert.Equal(1, result.Value.VersionHistory[1].VersionNumber);
    }

    [Fact]
    public async Task HandleAsync_Returns_EffectiveDate_Snapshot_Per_Version()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            new DateOnly(2026, 1, 1), null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        doc.ReplaceFile("key/v2.pdf", "v2.pdf", 200, "application/pdf", Guid.NewGuid(), Now.AddDays(1));
        db.SharedCompanyDocuments.Add(doc);

        db.SharedCompanyDocumentVersions.AddRange(
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 1, "key/v1.pdf", "v1.pdf", 100, "application/pdf", Guid.NewGuid(), Now, versionNote: null, requiresAcknowledgement: false, effectiveDate: new DateOnly(2026, 1, 1)),
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 2, "key/v2.pdf", "v2.pdf", 200, "application/pdf", Guid.NewGuid(), Now.AddDays(1), versionNote: "Updated section 3", requiresAcknowledgement: true, effectiveDate: new DateOnly(2026, 6, 1)));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        var v2 = Assert.Single(result.Value!.VersionHistory, v => v.VersionNumber == 2);
        var v1 = Assert.Single(result.Value.VersionHistory, v => v.VersionNumber == 1);
        Assert.Equal(new DateOnly(2026, 6, 1), v2.EffectiveDate);
        Assert.Equal(new DateOnly(2026, 1, 1), v1.EffectiveDate);
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Published")]
    [InlineData("Archived")]
    public async Task HandleAsync_Current_Version_PublicationStatus_Reflects_Document_Status(string expectedStatus)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);

        switch (expectedStatus)
        {
            case "Published":
                doc.Publish(Guid.NewGuid(), Now);
                break;
            case "Archived":
                doc.Publish(Guid.NewGuid(), Now);
                doc.Archive(Guid.NewGuid(), "Superseded", Now.AddDays(1));
                break;
        }

        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentVersions.Add(
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 1, "key/p.pdf", "p.pdf", 100, "application/pdf", Guid.NewGuid(), Now, versionNote: null, requiresAcknowledgement: false, effectiveDate: null));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        var current = Assert.Single(result.Value!.VersionHistory, v => v.VersionNumber == doc.VersionNumber);
        Assert.Equal(expectedStatus, current.PublicationStatus);
    }

    [Fact]
    public async Task HandleAsync_Older_Version_PublicationStatus_Is_Superseded()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        doc.ReplaceFile("key/v2.pdf", "v2.pdf", 200, "application/pdf", Guid.NewGuid(), Now.AddDays(1));
        db.SharedCompanyDocuments.Add(doc);

        db.SharedCompanyDocumentVersions.AddRange(
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 1, "key/v1.pdf", "v1.pdf", 100, "application/pdf", Guid.NewGuid(), Now, versionNote: null, requiresAcknowledgement: false, effectiveDate: null),
            SharedCompanyDocumentVersion.Create(Guid.NewGuid(), companyId, doc.Id, 2, "key/v2.pdf", "v2.pdf", 200, "application/pdf", Guid.NewGuid(), Now.AddDays(1), versionNote: "Updated section 3", requiresAcknowledgement: true, effectiveDate: null));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        var v1 = Assert.Single(result.Value!.VersionHistory, v => v.VersionNumber == 1);
        var v2 = Assert.Single(result.Value.VersionHistory, v => v.VersionNumber == 2);
        Assert.Equal("Superseded", v1.PublicationStatus);
        Assert.Equal("Published", v2.PublicationStatus);
    }

    [Fact]
    public async Task HandleAsync_Describes_Department_Audience()
    {
        await using var db = BuildContext();
        var companyId    = Guid.NewGuid();
        var category     = await SeedCategory(db, companyId);
        var departmentId = Guid.NewGuid();
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.Add(SharedCompanyDocumentAudienceRule.Create(
            Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Department, departmentId));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.DepartmentNames[departmentId] = "Engineering";

        var result = await Handler(db, audienceReader).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.Equal("Departments: Engineering", result.Value!.AudienceDescription);
        Assert.Equal([departmentId], result.Value.AudienceDepartmentIds);
    }

    [Fact]
    public async Task HandleAsync_Describes_Multiple_Audience_Rule_Types_Together()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var category        = await SeedCategory(db, companyId);
        var departmentId    = Guid.NewGuid();
        var locationId      = Guid.NewGuid();
        var positionId      = Guid.NewGuid();
        var employeeId      = Guid.NewGuid();
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, false, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAudienceRules.AddRange(
            SharedCompanyDocumentAudienceRule.Create(Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Department, departmentId),
            SharedCompanyDocumentAudienceRule.Create(Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Location, locationId),
            SharedCompanyDocumentAudienceRule.Create(Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Position, positionId),
            SharedCompanyDocumentAudienceRule.Create(Guid.NewGuid(), companyId, doc.Id, SharedCompanyDocumentAudienceRuleType.Employee, employeeId));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader();
        audienceReader.DepartmentNames[departmentId] = "Engineering";
        audienceReader.LocationNames[locationId] = "London";
        audienceReader.PositionProfileNames[positionId] = "Software Engineer";
        var names = new Dictionary<Guid, string> { [employeeId] = "Tom Williams" };

        var result = await Handler(db, audienceReader, names).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.Equal(
            "Departments: Engineering; Locations: London; Positions: Software Engineer; Employees: Tom Williams",
            result.Value!.AudienceDescription);
    }

    [Fact]
    public async Task HandleAsync_Returns_Acknowledgement_Progress_When_Required()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();
        var emp3 = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);

        db.SharedCompanyDocumentAcknowledgements.Add(
            SharedCompanyDocumentAcknowledgement.Create(Guid.NewGuid(), companyId, doc.Id, emp1, 1, "Statement", null, true, Now));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [emp1, emp2, emp3] };
        var names = new Dictionary<Guid, string> { [emp1] = "Tom Williams" };

        var result = await Handler(db, audienceReader, names).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.NotNull(result.Value!.AcknowledgementProgress);
        Assert.Equal(1, result.Value.AcknowledgementProgress!.AcknowledgedCount);
        Assert.Equal(3, result.Value.AcknowledgementProgress.EligibleCount);
        Assert.Equal(["Tom Williams"], result.Value.AcknowledgementProgress.AcknowledgedEmployeeNames);
    }

    [Fact]
    public async Task HandleAsync_Acknowledgement_Progress_Excludes_Old_Version_Acknowledgements()
    {
        // An employee acknowledged version 1; the document has since been replaced with version
        // 2 — that acknowledgement must not count towards the current version's progress.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var emp1 = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/v1.pdf", "v1.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        doc.ReplaceFile("key/v2.pdf", "v2.pdf", 200, "application/pdf", Guid.NewGuid(), Now.AddDays(1));
        db.SharedCompanyDocuments.Add(doc);

        db.SharedCompanyDocumentAcknowledgements.Add(
            SharedCompanyDocumentAcknowledgement.Create(Guid.NewGuid(), companyId, doc.Id, emp1, 1, "Statement", null, true, Now));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [emp1] };

        var result = await Handler(db, audienceReader).HandleAsync(
            new GetSharedCompanyDocumentRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.Equal(0, result.Value!.AcknowledgementProgress!.AcknowledgedCount);
    }

    private static GetSharedCompanyDocumentHandler Handler(
        DocumentsDbContext db, FakeEmployeeAudienceReader? audienceReader = null, Dictionary<Guid, string>? names = null)
    {
        var reader = audienceReader ?? new FakeEmployeeAudienceReader();
        var nameReader = new FakeEmployeeNameReader(names);
        return new GetSharedCompanyDocumentHandler(
            db,
            nameReader,
            new SharedCompanyDocumentAudienceMatcher(db, reader),
            new SharedCompanyDocumentAudienceDescriber(reader, nameReader));
    }

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
