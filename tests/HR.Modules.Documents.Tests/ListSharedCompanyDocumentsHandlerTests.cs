using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.ListSharedCompanyDocuments;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ListSharedCompanyDocumentsHandlerTests
{
    // Today is 2026-07-13.
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today      = DateOnly.FromDateTime(Now.UtcDateTime);

    private sealed class FakeEmployeeNameReader(Dictionary<Guid, string>? names = null) : IEmployeeNameReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(names ?? new Dictionary<Guid, string>());
    }

    [Fact]
    public async Task HandleAsync_Returns_Documents_Ordered_By_Most_Recently_Created()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var older = CreateDoc(companyId, "Older Policy", category.Id, "key/older.pdf", "older.pdf", null, null, Guid.NewGuid(), Now);
        var newer = CreateDoc(companyId, "Newer Policy", category.Id, "key/newer.pdf", "newer.pdf", null, null, Guid.NewGuid(), Now.AddMinutes(5));

        db.SharedCompanyDocuments.AddRange(older, newer);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Newer Policy", "Older Policy"], result.Value!.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task HandleAsync_Maps_Category_And_UpdatedBy_Names()
    {
        await using var db = BuildContext();
        var companyId  = Guid.NewGuid();
        var category   = await SeedCategory(db, companyId, "Handbook");
        var uploadedBy = Guid.NewGuid();

        var doc = CreateDoc(companyId, "Employee Handbook", category.Id, "key/handbook.pdf", "handbook.pdf", null, null, uploadedBy, Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var names  = new Dictionary<Guid, string> { [uploadedBy] = "Laura Bennett" };
        var result = await Handler(db, names).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal("Handbook",      result.Value!.Items[0].CategoryName);
        Assert.Equal("Laura Bennett", result.Value.Items[0].UpdatedByName);
        Assert.Equal(Now,             result.Value.Items[0].UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_Includes_Draft_Documents()
    {
        // The list endpoint backs the HR management screen, so drafts must be visible there —
        // unlike the employee-facing "published documents" list.
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var draft = CreateDoc(companyId, "Draft Policy", category.Id, "key/draft.pdf", "draft.pdf", null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(draft);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Draft", result.Value.Items[0].Status);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Documents_From_Other_Companies()
    {
        await using var db = BuildContext();
        var companyA  = Guid.NewGuid();
        var companyB  = Guid.NewGuid();
        var categoryA = await SeedCategory(db, companyA);
        var categoryB = await SeedCategory(db, companyB);

        db.SharedCompanyDocuments.AddRange(
            CreateDoc(companyA, "A Policy", categoryA.Id, "key/a.pdf", "a.pdf", null, null, Guid.NewGuid(), Now),
            CreateDoc(companyB, "B Policy", categoryB.Id, "key/b.pdf", "b.pdf", null, null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyA },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("A Policy", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_Company_Has_No_Documents()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var draft     = CreateDoc(companyId, "Draft Doc", category.Id, "key/d.pdf", "d.pdf", null, null, Guid.NewGuid(), Now);
        var published = CreateDoc(companyId, "Published Doc", category.Id, "key/p.pdf", "p.pdf", null, null, Guid.NewGuid(), Now);
        published.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(draft, published);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, Status = SharedCompanyDocumentStatus.Published },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Published Doc", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_CategoryId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var policy    = await SeedCategory(db, companyId, "Policy");
        var handbook  = await SeedCategory(db, companyId, "Handbook");

        db.SharedCompanyDocuments.AddRange(
            CreateDoc(companyId, "Policy Doc", policy.Id, "key/p.pdf", "p.pdf", null, null, Guid.NewGuid(), Now),
            CreateDoc(companyId, "Handbook Doc", handbook.Id, "key/h.pdf", "h.pdf", null, null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, CategoryId = handbook.Id },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Handbook Doc", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_ReviewDate_Range()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var dueSoon      = CreateDoc(companyId, "Due Soon", category.Id, "key/s.pdf", "s.pdf", null, new DateOnly(2026, 8, 1), Guid.NewGuid(), Now);
        var dueLater     = CreateDoc(companyId, "Due Later", category.Id, "key/l.pdf", "l.pdf", null, new DateOnly(2027, 1, 1), Guid.NewGuid(), Now);
        var noReviewDate = CreateDoc(companyId, "No Review Date", category.Id, "key/n.pdf", "n.pdf", null, null, Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(dueSoon, dueLater, noReviewDate);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest
            {
                CompanyId      = companyId,
                ReviewDateFrom = new DateOnly(2026, 1, 1),
                ReviewDateTo   = new DateOnly(2026, 12, 31),
            },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Due Soon", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Search_Title_Case_Insensitive()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        db.SharedCompanyDocuments.AddRange(
            CreateDoc(companyId, "Remote Working Policy", category.Id, "key/r.pdf", "r.pdf", null, null, Guid.NewGuid(), Now),
            CreateDoc(companyId, "Health and Safety Guide", category.Id, "key/h.pdf", "h.pdf", null, null, Guid.NewGuid(), Now));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, Search = "remote" },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Remote Working Policy", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_Maps_ReviewFrequency()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var doc = CreateDoc(
            companyId, "Monthly Reviewed Policy", category.Id, "key/monthly.pdf", "monthly.pdf", null, null, Guid.NewGuid(), Now,
            reviewFrequency: SharedCompanyDocumentReviewFrequency.Monthly);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal("Monthly", result.Value!.Items[0].ReviewFrequency);
    }

    [Fact]
    public async Task HandleAsync_Maps_ReviewOwner_Name_When_Present_In_Lookup()
    {
        await using var db = BuildContext();
        var companyId       = Guid.NewGuid();
        var category        = await SeedCategory(db, companyId);
        var reviewOwnerId   = Guid.NewGuid();

        var doc = CreateDoc(
            companyId, "Policy With Owner", category.Id, "key/owner.pdf", "owner.pdf", null, null, Guid.NewGuid(), Now,
            reviewOwnerEmployeeId: reviewOwnerId);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var names  = new Dictionary<Guid, string> { [reviewOwnerId] = "Priya Shah" };
        var result = await Handler(db, names).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(reviewOwnerId,   result.Value!.Items[0].ReviewOwnerEmployeeId);
        Assert.Equal("Priya Shah",    result.Value.Items[0].ReviewOwnerName);
    }

    [Fact]
    public async Task HandleAsync_ReviewOwnerName_Is_Null_When_No_ReviewOwnerEmployeeId()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var doc = CreateDoc(companyId, "Policy Without Owner", category.Id, "key/noowner.pdf", "noowner.pdf", null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Null(result.Value!.Items[0].ReviewOwnerEmployeeId);
        Assert.Null(result.Value.Items[0].ReviewOwnerName);
    }

    [Fact]
    public async Task HandleAsync_ReviewOwnerName_Falls_Back_To_Unknown_When_Not_In_Lookup()
    {
        await using var db = BuildContext();
        var companyId     = Guid.NewGuid();
        var category      = await SeedCategory(db, companyId);
        var reviewOwnerId = Guid.NewGuid();

        var doc = CreateDoc(
            companyId, "Policy With Unresolvable Owner", category.Id, "key/unresolved.pdf", "unresolved.pdf", null, null, Guid.NewGuid(), Now,
            reviewOwnerEmployeeId: reviewOwnerId);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        // No entry for reviewOwnerId in the lookup dictionary.
        var result = await Handler(db, new Dictionary<Guid, string>()).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(reviewOwnerId, result.Value!.Items[0].ReviewOwnerEmployeeId);
        Assert.Equal("Unknown",     result.Value.Items[0].ReviewOwnerName);
    }

    [Fact]
    public async Task HandleAsync_Resolves_UpdatedBy_And_ReviewOwner_Names_From_Single_Shared_Lookup()
    {
        await using var db = BuildContext();
        var companyId     = Guid.NewGuid();
        var category      = await SeedCategory(db, companyId);
        var updatedBy     = Guid.NewGuid();
        var reviewOwnerId = Guid.NewGuid();

        var doc = CreateDoc(
            companyId, "Policy With Both Names", category.Id, "key/both.pdf", "both.pdf", null, null, updatedBy, Now,
            reviewOwnerEmployeeId: reviewOwnerId);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var names = new Dictionary<Guid, string>
        {
            [updatedBy]     = "Laura Bennett",
            [reviewOwnerId] = "Priya Shah",
        };
        var result = await Handler(db, names).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal("Laura Bennett", result.Value!.Items[0].UpdatedByName);
        Assert.Equal("Priya Shah",    result.Value.Items[0].ReviewOwnerName);
        Assert.Equal(reviewOwnerId,   result.Value.Items[0].ReviewOwnerEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_ReviewStatusFilter_DueSoon_Returns_Only_NonTerminal_Documents_Due_Within_Next_Week()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var dueToday      = CreateDoc(companyId, "Due Today", category.Id, "key/1.pdf", "1.pdf", null, Today, Guid.NewGuid(), Now);
        var dueInSevenDays = CreateDoc(companyId, "Due In Seven Days", category.Id, "key/2.pdf", "2.pdf", null, Today.AddDays(7), Guid.NewGuid(), Now);
        var overdue       = CreateDoc(companyId, "Overdue", category.Id, "key/3.pdf", "3.pdf", null, Today.AddDays(-1), Guid.NewGuid(), Now);
        var beyondWindow  = CreateDoc(companyId, "Beyond Window", category.Id, "key/4.pdf", "4.pdf", null, Today.AddDays(8), Guid.NewGuid(), Now);
        var noReviewDate  = CreateDoc(companyId, "No Review Date", category.Id, "key/5.pdf", "5.pdf", null, null, Guid.NewGuid(), Now);

        var archivedInWindow = CreateDoc(companyId, "Archived In Window", category.Id, "key/6.pdf", "6.pdf", null, Today.AddDays(2), Guid.NewGuid(), Now);
        archivedInWindow.Archive(Guid.NewGuid(), "No longer needed", Now);

        var expiredInWindow = CreateDoc(companyId, "Expired In Window", category.Id, "key/7.pdf", "7.pdf", null, Today.AddDays(2), Guid.NewGuid(), Now);
        expiredInWindow.MarkExpired(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(
            dueToday, dueInSevenDays, overdue, beyondWindow, noReviewDate, archivedInWindow, expiredInWindow);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, ReviewStatusFilter = SharedCompanyDocumentReviewStatusFilter.DueSoon },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["Due In Seven Days", "Due Today"],
            result.Value!.Items.Select(i => i.Title).OrderBy(t => t));
    }

    [Fact]
    public async Task HandleAsync_ReviewStatusFilter_Overdue_Returns_Only_NonTerminal_Documents_With_Past_ReviewDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var overdue      = CreateDoc(companyId, "Overdue", category.Id, "key/1.pdf", "1.pdf", null, Today.AddDays(-1), Guid.NewGuid(), Now);
        var dueToday     = CreateDoc(companyId, "Due Today", category.Id, "key/2.pdf", "2.pdf", null, Today, Guid.NewGuid(), Now);
        var dueSoon      = CreateDoc(companyId, "Due Soon", category.Id, "key/3.pdf", "3.pdf", null, Today.AddDays(3), Guid.NewGuid(), Now);
        var noReviewDate = CreateDoc(companyId, "No Review Date", category.Id, "key/4.pdf", "4.pdf", null, null, Guid.NewGuid(), Now);

        var archivedOverdue = CreateDoc(companyId, "Archived Overdue", category.Id, "key/5.pdf", "5.pdf", null, Today.AddDays(-5), Guid.NewGuid(), Now);
        archivedOverdue.Archive(Guid.NewGuid(), "No longer needed", Now);

        var expiredOverdue = CreateDoc(companyId, "Expired Overdue", category.Id, "key/6.pdf", "6.pdf", null, Today.AddDays(-5), Guid.NewGuid(), Now);
        expiredOverdue.MarkExpired(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(overdue, dueToday, dueSoon, noReviewDate, archivedOverdue, expiredOverdue);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, ReviewStatusFilter = SharedCompanyDocumentReviewStatusFilter.Overdue },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Overdue", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_ReviewStatusFilter_NoReview_Returns_Only_NonTerminal_Documents_With_Null_ReviewDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var noReviewDate = CreateDoc(companyId, "No Review Date", category.Id, "key/1.pdf", "1.pdf", null, null, Guid.NewGuid(), Now);
        var overdue      = CreateDoc(companyId, "Overdue", category.Id, "key/2.pdf", "2.pdf", null, Today.AddDays(-1), Guid.NewGuid(), Now);
        var dueSoon      = CreateDoc(companyId, "Due Soon", category.Id, "key/3.pdf", "3.pdf", null, Today.AddDays(3), Guid.NewGuid(), Now);

        var archivedNoReviewDate = CreateDoc(companyId, "Archived No Review Date", category.Id, "key/4.pdf", "4.pdf", null, null, Guid.NewGuid(), Now);
        archivedNoReviewDate.Archive(Guid.NewGuid(), "No longer needed", Now);

        var expiredNoReviewDate = CreateDoc(companyId, "Expired No Review Date", category.Id, "key/5.pdf", "5.pdf", null, null, Guid.NewGuid(), Now);
        expiredNoReviewDate.MarkExpired(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(noReviewDate, overdue, dueSoon, archivedNoReviewDate, expiredNoReviewDate);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, ReviewStatusFilter = SharedCompanyDocumentReviewStatusFilter.NoReview },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("No Review Date", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_ReviewStatusFilter_Expired_Returns_Only_Expired_Documents_Regardless_Of_ReviewDate()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var expiredWithNullReviewDate = CreateDoc(companyId, "Expired Null Review Date", category.Id, "key/1.pdf", "1.pdf", null, null, Guid.NewGuid(), Now);
        expiredWithNullReviewDate.MarkExpired(Guid.NewGuid(), Now);

        var expiredWithPastReviewDate = CreateDoc(companyId, "Expired Past Review Date", category.Id, "key/2.pdf", "2.pdf", null, Today.AddDays(-30), Guid.NewGuid(), Now);
        expiredWithPastReviewDate.MarkExpired(Guid.NewGuid(), Now);

        var expiredWithFutureReviewDate = CreateDoc(companyId, "Expired Future Review Date", category.Id, "key/3.pdf", "3.pdf", null, Today.AddDays(30), Guid.NewGuid(), Now);
        expiredWithFutureReviewDate.MarkExpired(Guid.NewGuid(), Now);

        var publishedOverdue = CreateDoc(companyId, "Published Overdue", category.Id, "key/4.pdf", "4.pdf", null, Today.AddDays(-1), Guid.NewGuid(), Now);
        publishedOverdue.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(
            expiredWithNullReviewDate, expiredWithPastReviewDate, expiredWithFutureReviewDate, publishedOverdue);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, ReviewStatusFilter = SharedCompanyDocumentReviewStatusFilter.Expired },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Items.Count);
        Assert.DoesNotContain(result.Value.Items, i => i.Title == "Published Overdue");
    }

    [Fact]
    public async Task HandleAsync_ReviewStatusFilter_Null_Does_Not_Affect_Existing_Filtering_Behavior()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var overdue      = CreateDoc(companyId, "Overdue", category.Id, "key/1.pdf", "1.pdf", null, Today.AddDays(-1), Guid.NewGuid(), Now);
        var dueSoon      = CreateDoc(companyId, "Due Soon", category.Id, "key/2.pdf", "2.pdf", null, Today.AddDays(3), Guid.NewGuid(), Now);
        var noReviewDate = CreateDoc(companyId, "No Review Date", category.Id, "key/3.pdf", "3.pdf", null, null, Guid.NewGuid(), Now);

        var expired = CreateDoc(companyId, "Expired", category.Id, "key/4.pdf", "4.pdf", null, Today.AddDays(-1), Guid.NewGuid(), Now);
        expired.MarkExpired(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(overdue, dueSoon, noReviewDate, expired);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, ReviewStatusFilter = null },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_ReviewStatusFilter_Combines_With_CategoryId_Filter()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var policy    = await SeedCategory(db, companyId, "Policy");
        var handbook  = await SeedCategory(db, companyId, "Handbook");

        var overduePolicy   = CreateDoc(companyId, "Overdue Policy", policy.Id, "key/1.pdf", "1.pdf", null, Today.AddDays(-1), Guid.NewGuid(), Now);
        var overdueHandbook = CreateDoc(companyId, "Overdue Handbook", handbook.Id, "key/2.pdf", "2.pdf", null, Today.AddDays(-1), Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(overduePolicy, overdueHandbook);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest
            {
                CompanyId          = companyId,
                CategoryId         = policy.Id,
                ReviewStatusFilter = SharedCompanyDocumentReviewStatusFilter.Overdue,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Overdue Policy", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_ReviewStatusFilter_Combines_With_Status_Filter()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var draftOverdue = CreateDoc(companyId, "Draft Overdue", category.Id, "key/1.pdf", "1.pdf", null, Today.AddDays(-1), Guid.NewGuid(), Now);

        var publishedOverdue = CreateDoc(companyId, "Published Overdue", category.Id, "key/2.pdf", "2.pdf", null, Today.AddDays(-1), Guid.NewGuid(), Now);
        publishedOverdue.Publish(Guid.NewGuid(), Now);

        db.SharedCompanyDocuments.AddRange(draftOverdue, publishedOverdue);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest
            {
                CompanyId          = companyId,
                Status             = SharedCompanyDocumentStatus.Published,
                ReviewStatusFilter = SharedCompanyDocumentReviewStatusFilter.Overdue,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Items);
        Assert.Equal("Published Overdue", result.Value.Items[0].Title);
    }

    [Fact]
    public async Task HandleAsync_ReviewStatusFilter_DueSoon_Boundary_Today_Is_DueSoon_Not_Overdue()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var dueToday = CreateDoc(companyId, "Due Today", category.Id, "key/1.pdf", "1.pdf", null, Today, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(dueToday);
        await db.SaveChangesAsync();

        var dueSoonResult = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, ReviewStatusFilter = SharedCompanyDocumentReviewStatusFilter.DueSoon },
            CancellationToken.None);
        var overdueResult = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, ReviewStatusFilter = SharedCompanyDocumentReviewStatusFilter.Overdue },
            CancellationToken.None);

        Assert.Single(dueSoonResult.Value!.Items);
        Assert.Empty(overdueResult.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_ReviewStatusFilter_DueSoon_Boundary_Seven_Days_Out_Is_Still_DueSoon()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var dueInSevenDays = CreateDoc(companyId, "Due In Seven Days", category.Id, "key/1.pdf", "1.pdf", null, Today.AddDays(7), Guid.NewGuid(), Now);
        var dueInEightDays = CreateDoc(companyId, "Due In Eight Days", category.Id, "key/2.pdf", "2.pdf", null, Today.AddDays(8), Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.AddRange(dueInSevenDays, dueInEightDays);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new ListSharedCompanyDocumentsRequest { CompanyId = companyId, ReviewStatusFilter = SharedCompanyDocumentReviewStatusFilter.DueSoon },
            CancellationToken.None);

        Assert.Single(result.Value!.Items);
        Assert.Equal("Due In Seven Days", result.Value.Items[0].Title);
    }

    private static ListSharedCompanyDocumentsHandler Handler(
        DocumentsDbContext db, Dictionary<Guid, string>? names = null, IClock? clock = null) =>
        new(db, new FakeEmployeeNameReader(names), clock ?? new FakeClock(Now.UtcDateTime));

    private static SharedCompanyDocument CreateDoc(
        Guid companyId, string title, Guid categoryId, string storageKey, string fileName,
        DateOnly? effectiveDate, DateOnly? reviewDate, Guid createdBy, DateTimeOffset now,
        SharedCompanyDocumentReviewFrequency reviewFrequency = SharedCompanyDocumentReviewFrequency.None,
        Guid? reviewOwnerEmployeeId = null) =>
        SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, title, null, categoryId, storageKey, fileName, 100, "application/pdf",
            effectiveDate, reviewDate, reviewFrequency, null, reviewOwnerEmployeeId, false, null, null, createdBy, now);

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
