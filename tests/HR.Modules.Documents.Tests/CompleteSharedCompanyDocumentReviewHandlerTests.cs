using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.CompleteSharedCompanyDocumentReview;
using HR.Modules.Documents.Features.ListSharedCompanyDocumentsDueForReview;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class CompleteSharedCompanyDocumentReviewHandlerTests
{
    // Today is 2026-07-16.
    private static readonly DateTime FixedUtcNow = new(2026, 7, 16, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today        = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateTimeOffset Now    = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Successfully_Records_Review_Fields()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var reviewedBy = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid(), SharedCompanyDocumentReviewFrequency.Yearly, null, Today.AddDays(-1));
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = companyId,
                DocumentId = doc.Id,
                ReviewNotes = "  Reviewed against latest legislation.  ",
            },
            reviewedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(doc.Id, result.Value!.Id);
        Assert.Equal(companyId, result.Value.CompanyId);
        Assert.Equal(Today, result.Value.LastReviewedAt);
        Assert.Equal(reviewedBy, result.Value.LastReviewedByEmployeeId);
        Assert.Equal("Reviewed against latest legislation.", result.Value.LastReviewNotes);
        Assert.Equal(Today.AddMonths(12), result.Value.ReviewDate);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(Today, stored.LastReviewedAt);
        Assert.Equal(reviewedBy, stored.LastReviewedByEmployeeId);
        Assert.Equal("Reviewed against latest legislation.", stored.LastReviewNotes);
        Assert.Equal(Today.AddMonths(12), stored.ReviewDate);
        Assert.Equal(reviewedBy, stored.UpdatedBy);
        Assert.Equal(FixedUtcNow, stored.UpdatedAt);
    }

    [Theory]
    [MemberData(nameof(FrequencyCases))]
    public async Task HandleAsync_Computes_Correct_Next_ReviewDate(
        int frequencyValue, int? customMonths, DateOnly? expectedNextReviewDate)
    {
        var frequency = (SharedCompanyDocumentReviewFrequency)frequencyValue;
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid(), frequency, customMonths, Today.AddDays(-30));
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = companyId,
                DocumentId = doc.Id,
                ReviewNotes = "Reviewed.",
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedNextReviewDate, result.Value!.ReviewDate);

        var stored = await db.SharedCompanyDocuments.AsNoTracking().SingleAsync(d => d.Id == doc.Id);
        Assert.Equal(expectedNextReviewDate, stored.ReviewDate);
    }

    // MemberData/InlineData rows must not expose SharedCompanyDocumentReviewFrequency directly:
    // it is internal, and a public [Theory] method's parameters must be at least as accessible
    // as the method itself (CS0051), even between InternalsVisibleTo friend assemblies. The
    // underlying int value is passed instead and cast back to the enum inside the test method.
    public static IEnumerable<object?[]> FrequencyCases()
    {
        yield return new object?[] { (int)SharedCompanyDocumentReviewFrequency.Monthly, null, Today.AddMonths(1) };
        yield return new object?[] { (int)SharedCompanyDocumentReviewFrequency.Quarterly, null, Today.AddMonths(3) };
        yield return new object?[] { (int)SharedCompanyDocumentReviewFrequency.SixMonthly, null, Today.AddMonths(6) };
        yield return new object?[] { (int)SharedCompanyDocumentReviewFrequency.Yearly, null, Today.AddMonths(12) };
        yield return new object?[] { (int)SharedCompanyDocumentReviewFrequency.Custom, 4, Today.AddMonths(4) };
        yield return new object?[] { (int)SharedCompanyDocumentReviewFrequency.None, null, null };
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Unknown_Document()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = Guid.NewGuid(),
                DocumentId = Guid.NewGuid(),
                ReviewNotes = "Reviewed.",
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Document_Belongs_To_Another_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid(), SharedCompanyDocumentReviewFrequency.None, null, null);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = Guid.NewGuid(),
                DocumentId = doc.Id,
                ReviewNotes = "Reviewed.",
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Completing_Review_Removes_Document_From_DueForReview_List()
    {
        // End-to-end proof that completing a review clears the document's overdue status:
        // seed an overdue document, confirm it appears in the due-for-review list beforehand,
        // complete its review, then confirm it no longer appears afterwards.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid(), SharedCompanyDocumentReviewFrequency.Yearly, null, Today.AddDays(-10));
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var beforeResult = await DueForReviewHandler(db).HandleAsync(
            new ListSharedCompanyDocumentsDueForReviewRequest(companyId), CancellationToken.None);
        Assert.True(beforeResult.IsSuccess);
        Assert.Single(beforeResult.Value!.Items);

        var completeResult = await Handler(db).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = companyId,
                DocumentId = doc.Id,
                ReviewNotes = "All good, no changes required.",
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(completeResult.IsSuccess);
        Assert.NotNull(completeResult.Value!.ReviewDate);
        Assert.True(completeResult.Value.ReviewDate > Today);

        var afterResult = await DueForReviewHandler(db).HandleAsync(
            new ListSharedCompanyDocumentsDueForReviewRequest(companyId), CancellationToken.None);
        Assert.True(afterResult.IsSuccess);
        Assert.Empty(afterResult.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Creates_ReviewHistory_Row_With_Correct_Fields()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var reviewedBy = Guid.NewGuid();
        var previousReviewDate = Today.AddDays(-1);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid(), SharedCompanyDocumentReviewFrequency.Yearly, null, previousReviewDate);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = companyId,
                DocumentId = doc.Id,
                ReviewNotes = "  Reviewed against latest legislation.  ",
            },
            reviewedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var history = await db.SharedCompanyDocumentReviewHistories.AsNoTracking()
            .Where(h => h.SharedCompanyDocumentId == doc.Id)
            .ToListAsync();
        var entry = Assert.Single(history);
        Assert.Equal(companyId, entry.CompanyId);
        Assert.Equal(doc.Id, entry.SharedCompanyDocumentId);
        Assert.Equal(Today, entry.ReviewDate);
        Assert.Equal(reviewedBy, entry.ReviewedByEmployeeId);
        Assert.Equal("Reviewed against latest legislation.", entry.ReviewNotes);
        Assert.Equal(previousReviewDate, entry.PreviousReviewDate);
    }

    [Fact]
    public async Task HandleAsync_Second_Review_Adds_Second_History_Row_Without_Overwriting_First()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid(), SharedCompanyDocumentReviewFrequency.Yearly, null, Today.AddDays(-1));
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var firstResult = await Handler(db).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = companyId,
                DocumentId = doc.Id,
                ReviewNotes = "First review.",
            },
            Guid.NewGuid(), CancellationToken.None);
        Assert.True(firstResult.IsSuccess);
        var reviewDateAfterFirst = firstResult.Value!.ReviewDate;

        var secondResult = await Handler(db).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = companyId,
                DocumentId = doc.Id,
                ReviewNotes = "Second review.",
            },
            Guid.NewGuid(), CancellationToken.None);
        Assert.True(secondResult.IsSuccess);

        // Both reviews run against the same FakeClock instant, so CreatedAt/ReviewDate are
        // identical between the two rows — distinguish them by ReviewNotes instead of relying
        // on any ordering.
        var history = await db.SharedCompanyDocumentReviewHistories.AsNoTracking()
            .Where(h => h.SharedCompanyDocumentId == doc.Id)
            .ToListAsync();
        Assert.Equal(2, history.Count);
        var firstEntry  = Assert.Single(history, h => h.ReviewNotes == "First review.");
        var secondEntry = Assert.Single(history, h => h.ReviewNotes == "Second review.");
        Assert.Equal(reviewDateAfterFirst, secondEntry.PreviousReviewDate);
        Assert.NotEqual(reviewDateAfterFirst, firstEntry.PreviousReviewDate);
    }

    [Fact]
    public async Task HandleAsync_First_Review_With_Null_Prior_ReviewDate_Produces_Null_PreviousReviewDate()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid(), SharedCompanyDocumentReviewFrequency.None, null, null);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = companyId,
                DocumentId = doc.Id,
                ReviewNotes = "Reviewed.",
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var entry = await db.SharedCompanyDocumentReviewHistories.AsNoTracking()
            .SingleAsync(h => h.SharedCompanyDocumentId == doc.Id);
        Assert.Null(entry.PreviousReviewDate);
    }

    [Fact]
    public async Task HandleAsync_Publishes_Audit_Event()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var reviewedBy = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid(), SharedCompanyDocumentReviewFrequency.Yearly, null, Today.AddDays(-1));
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audit = new FakeAuditPublisher();
        var result = await Handler(db, audit).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = companyId,
                DocumentId = doc.Id,
                ReviewNotes = "Reviewed against latest legislation.",
            },
            reviewedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var evt = Assert.Single(audit.Published);
        Assert.Equal("shared_company_document.review_completed", evt.EventType);
        Assert.Equal("SharedCompanyDocument",                    evt.EntityType);
        Assert.Equal(doc.Id,                                     evt.EntityId);
        Assert.Equal(reviewedBy,                                 evt.ActorUserId);
    }

    [Fact]
    public async Task HandleAsync_Audit_Event_Before_After_Reflects_ReviewDate_And_Notes_Change()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var reviewedBy = Guid.NewGuid();
        var previousReviewDate = Today.AddDays(-1);
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid(), SharedCompanyDocumentReviewFrequency.Yearly, null, previousReviewDate);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audit = new FakeAuditPublisher();
        var result = await Handler(db, audit).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = companyId,
                DocumentId = doc.Id,
                ReviewNotes = "Reviewed against latest legislation.",
            },
            reviewedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var expectedNextReviewDate = Today.AddMonths(12);
        Assert.Equal(expectedNextReviewDate, result.Value!.ReviewDate);

        var evt = Assert.Single(audit.Published);

        // Before carries the review date this review fulfilled; After carries the new review
        // date/notes/next review date — serialize both and confirm each value only shows up
        // where expected, mirroring UpdateSharedCompanyDocumentMetadataHandlerTests' Before/After
        // JSON assertion style.
        var beforeJson = JsonSerializer.Serialize(evt.Before);
        var afterJson  = JsonSerializer.Serialize(evt.After);

        Assert.Contains(previousReviewDate.ToString("yyyy-MM-dd"), beforeJson);
        Assert.DoesNotContain(expectedNextReviewDate.ToString("yyyy-MM-dd"), beforeJson);

        Assert.Contains(Today.ToString("yyyy-MM-dd"), afterJson);
        Assert.Contains(expectedNextReviewDate.ToString("yyyy-MM-dd"), afterJson);
        Assert.Contains("Reviewed against latest legislation.", afterJson);
        Assert.DoesNotContain(previousReviewDate.ToString("yyyy-MM-dd"), afterJson);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Publish_Audit_Event_On_Failure()
    {
        await using var db = BuildContext();

        var audit = new FakeAuditPublisher();
        await Handler(db, audit).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = Guid.NewGuid(),
                DocumentId = Guid.NewGuid(),
                ReviewNotes = "Reviewed.",
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(audit.Published);
    }

    [Fact]
    public async Task HandleAsync_Completes_Open_Review_Task_By_Source_Entity()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId);
        var reviewedBy = Guid.NewGuid();
        var doc = CreateDoc(companyId, category.Id, Guid.NewGuid(), SharedCompanyDocumentReviewFrequency.Yearly, null, Today.AddDays(-1));
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var taskCompleter = new FakeTaskCompleter();
        var result = await Handler(db, taskCompleter: taskCompleter).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = companyId,
                DocumentId = doc.Id,
                ReviewNotes = "Reviewed against latest legislation.",
            },
            reviewedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var call = Assert.Single(taskCompleter.Calls);
        Assert.Equal(doc.CompanyId, call.CompanyId);
        Assert.Equal(doc.Id,        call.SourceEntityId);
        Assert.Equal(TaskSource.Document, call.Source);
        Assert.Equal(TaskActionType.Review, call.ActionType);
        Assert.Equal(reviewedBy, call.CompletedBy);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Complete_Task_On_Failure()
    {
        await using var db = BuildContext();

        var taskCompleter = new FakeTaskCompleter();
        await Handler(db, taskCompleter: taskCompleter).HandleAsync(
            new CompleteSharedCompanyDocumentReviewRequest
            {
                CompanyId = Guid.NewGuid(),
                DocumentId = Guid.NewGuid(),
                ReviewNotes = "Reviewed.",
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(taskCompleter.Calls);
    }

    private static CompleteSharedCompanyDocumentReviewHandler Handler(
        DocumentsDbContext db, FakeAuditPublisher? auditPublisher = null, FakeTaskCompleter? taskCompleter = null) =>
        new(db, taskCompleter ?? new FakeTaskCompleter(), auditPublisher ?? new FakeAuditPublisher(), new FakeClock(FixedUtcNow));

    private static ListSharedCompanyDocumentsDueForReviewHandler DueForReviewHandler(DocumentsDbContext db) =>
        new(db, new FakeClock(FixedUtcNow), new FakeEmployeeNameReader());

    private static SharedCompanyDocument CreateDoc(
        Guid companyId, Guid categoryId, Guid createdBy,
        SharedCompanyDocumentReviewFrequency reviewFrequency, int? customReviewFrequencyMonths, DateOnly? reviewDate) =>
        SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, categoryId, $"key/{Guid.NewGuid():N}.pdf", "p.pdf", 100,
            "application/pdf", null, reviewDate, reviewFrequency, customReviewFrequencyMonths, null, false, null,
            null, createdBy, Now);

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
