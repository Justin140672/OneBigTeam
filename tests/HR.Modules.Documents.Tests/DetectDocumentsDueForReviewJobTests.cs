using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Jobs;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class DetectDocumentsDueForReviewJobTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today     = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static DetectDocumentsDueForReviewJob BuildJob(DocumentsDbContext db, FakeLogger<DetectDocumentsDueForReviewJob> logger) =>
        new(db, new FakeClock(FixedUtcNow), logger);

    private static async Task<CompanyDocumentCategory> SeedCategoryAsync(DocumentsDbContext db, Guid companyId)
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", Now);
        db.CompanyDocumentCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    private static SharedCompanyDocument CreateDoc(
        Guid companyId, Guid categoryId, DateOnly? reviewDate,
        SharedCompanyDocumentStatus status = SharedCompanyDocumentStatus.Published)
    {
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Employee Handbook", null, categoryId, $"key/{Guid.NewGuid():N}.pdf",
            "handbook.pdf", 100, "application/pdf", null, reviewDate, SharedCompanyDocumentReviewFrequency.None,
            null, null, false, null, null, Guid.NewGuid(), Now);

        if (status is SharedCompanyDocumentStatus.Published or SharedCompanyDocumentStatus.Archived)
            doc.Publish(Guid.NewGuid(), Now);

        if (status == SharedCompanyDocumentStatus.Archived)
            doc.Archive(Guid.NewGuid(), "Superseded", Now);

        return doc;
    }

    private static int LoggedDueCount(FakeLogger<DetectDocumentsDueForReviewJob> logger)
    {
        var message = Assert.Single(logger.Messages);
        var match = System.Text.RegularExpressions.Regex.Match(message, @"found (\d+) shared company document");
        Assert.True(match.Success, $"Expected message to contain a due-count, got: {message}");
        return int.Parse(match.Groups[1].Value);
    }

    [Fact]
    public async Task ExecuteAsync_Counts_Document_With_ReviewDate_In_The_Past()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        db.SharedCompanyDocuments.Add(CreateDoc(companyId, category.Id, Today.AddDays(-5)));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        await BuildJob(db, logger).ExecuteAsync();

        Assert.Equal(1, LoggedDueCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Counts_Document_With_ReviewDate_Equal_To_Today()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        db.SharedCompanyDocuments.Add(CreateDoc(companyId, category.Id, Today));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        await BuildJob(db, logger).ExecuteAsync();

        Assert.Equal(1, LoggedDueCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Count_Document_With_ReviewDate_In_The_Future()
    {
        // Represents a review that has already been completed by moving ReviewDate forward.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        db.SharedCompanyDocuments.Add(CreateDoc(companyId, category.Id, Today.AddDays(5)));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        await BuildJob(db, logger).ExecuteAsync();

        Assert.Equal(0, LoggedDueCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Count_Document_With_Null_ReviewDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        db.SharedCompanyDocuments.Add(CreateDoc(companyId, category.Id, reviewDate: null));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        await BuildJob(db, logger).ExecuteAsync();

        Assert.Equal(0, LoggedDueCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Does_Not_Count_Archived_Document_Even_When_Overdue()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategoryAsync(db, companyId);

        db.SharedCompanyDocuments.Add(
            CreateDoc(companyId, category.Id, Today.AddDays(-10), SharedCompanyDocumentStatus.Archived));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        await BuildJob(db, logger).ExecuteAsync();

        Assert.Equal(0, LoggedDueCount(logger));
    }

    [Fact]
    public async Task ExecuteAsync_Counts_Documents_Across_Multiple_Companies_In_A_Single_Run()
    {
        // This job queries across ALL companies (no CompanyId filter), unlike the per-company
        // ListSharedCompanyDocumentsDueForReviewHandler — confirm no accidental company-scoping crept in.
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var categoryA = await SeedCategoryAsync(db, companyA);
        var categoryB = await SeedCategoryAsync(db, companyB);

        db.SharedCompanyDocuments.AddRange(
            CreateDoc(companyA, categoryA.Id, Today.AddDays(-1)),
            CreateDoc(companyB, categoryB.Id, Today));
        await db.SaveChangesAsync();

        var logger = new FakeLogger<DetectDocumentsDueForReviewJob>();
        await BuildJob(db, logger).ExecuteAsync();

        Assert.Equal(2, LoggedDueCount(logger));
    }
}
