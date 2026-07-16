using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetSharedCompanyDocumentAcknowledgementProgress;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetSharedCompanyDocumentAcknowledgementProgressHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeEmployeeNameReader(Dictionary<Guid, string>? names = null) : IEmployeeNameReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(names ?? new Dictionary<Guid, string>());
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_For_Missing_Document()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new GetSharedCompanyDocumentAcknowledgementProgressRequest { CompanyId = Guid.NewGuid(), DocumentId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_Error_When_Document_Does_Not_Require_Acknowledgement()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: false, null, null, Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetSharedCompanyDocumentAcknowledgementProgressRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Computes_Acknowledged_And_Outstanding_When_Due_Date_Has_Not_Passed()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var acknowledgedEmployee = Guid.NewGuid();
        var outstandingEmployee  = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2027, 1, 1),
            acknowledgementStatement: null, createdBy: Guid.NewGuid(), now: Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAcknowledgements.Add(
            SharedCompanyDocumentAcknowledgement.Create(
                Guid.NewGuid(), companyId, doc.Id, acknowledgedEmployee, 1, "Statement", null, Now));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader
        {
            EligibleEmployeeIds = [acknowledgedEmployee, outstandingEmployee],
        };

        var names = new Dictionary<Guid, string>
        {
            [acknowledgedEmployee] = "Ada Acknowledged",
            [outstandingEmployee]  = "Owen Outstanding",
        };

        var result = await Handler(db, audienceReader, names).HandleAsync(
            new GetSharedCompanyDocumentAcknowledgementProgressRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalAssigned);
        Assert.Equal(1, result.Value.AcknowledgedCount);
        Assert.Equal(1, result.Value.OutstandingCount);
        Assert.Equal(0, result.Value.OverdueCount);
        Assert.Equal(50m, result.Value.AcknowledgementPercentage);

        var outstandingItem = result.Value.Items.Single(i => i.EmployeeId == outstandingEmployee);
        Assert.Equal("Outstanding", outstandingItem.Status);
        Assert.Null(outstandingItem.AcknowledgedAt);

        var acknowledgedItem = result.Value.Items.Single(i => i.EmployeeId == acknowledgedEmployee);
        Assert.Equal("Acknowledged", acknowledgedItem.Status);
        Assert.Equal(Now, acknowledgedItem.AcknowledgedAt);
    }

    [Fact]
    public async Task HandleAsync_Computes_Overdue_When_Due_Date_Has_Passed_And_Employee_Has_Not_Acknowledged()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var acknowledgedEmployee = Guid.NewGuid();
        var overdueEmployee      = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2026, 7, 1),
            acknowledgementStatement: null, createdBy: Guid.NewGuid(), now: Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAcknowledgements.Add(
            SharedCompanyDocumentAcknowledgement.Create(
                Guid.NewGuid(), companyId, doc.Id, acknowledgedEmployee, 1, "Statement", null, Now));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader
        {
            EligibleEmployeeIds = [acknowledgedEmployee, overdueEmployee],
        };

        var result = await Handler(db, audienceReader).HandleAsync(
            new GetSharedCompanyDocumentAcknowledgementProgressRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalAssigned);
        Assert.Equal(1, result.Value.AcknowledgedCount);
        Assert.Equal(0, result.Value.OutstandingCount);
        Assert.Equal(1, result.Value.OverdueCount);

        var overdueItem = result.Value.Items.Single(i => i.EmployeeId == overdueEmployee);
        Assert.Equal("Overdue", overdueItem.Status);
        Assert.Null(overdueItem.AcknowledgedAt);
    }

    [Fact]
    public async Task HandleAsync_Zero_Assigned_Employees_Produces_Zero_Percentage_Not_A_Division_Error()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2027, 1, 1),
            acknowledgementStatement: null, createdBy: Guid.NewGuid(), now: Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader { EligibleEmployeeIds = [] };

        var result = await Handler(db, audienceReader).HandleAsync(
            new GetSharedCompanyDocumentAcknowledgementProgressRequest { CompanyId = companyId, DocumentId = doc.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TotalAssigned);
        Assert.Equal(0m, result.Value.AcknowledgementPercentage);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Department_While_Summary_Counts_Stay_Unfiltered()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var salesDeptId     = Guid.NewGuid();
        var engineeringDeptId = Guid.NewGuid();

        var salesEmployee       = Guid.NewGuid();
        var engineeringEmployee = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2027, 1, 1),
            acknowledgementStatement: null, createdBy: Guid.NewGuid(), now: Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader
        {
            EligibleEmployeeIds = [salesEmployee, engineeringEmployee],
        };
        audienceReader.EmployeeAudiences[salesEmployee]       = new EmployeeAudienceProfile(salesDeptId, null, null);
        audienceReader.EmployeeAudiences[engineeringEmployee] = new EmployeeAudienceProfile(engineeringDeptId, null, null);

        var result = await Handler(db, audienceReader).HandleAsync(
            new GetSharedCompanyDocumentAcknowledgementProgressRequest
            {
                CompanyId = companyId, DocumentId = doc.Id, DepartmentId = salesDeptId,
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.TotalAssigned);
        Assert.Single(result.Value.Items);
        Assert.Equal(salesEmployee, result.Value.Items[0].EmployeeId);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Overdue_Status()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var category  = await SeedCategory(db, companyId);

        var acknowledgedEmployee = Guid.NewGuid();
        var overdueEmployeeOne   = Guid.NewGuid();
        var overdueEmployeeTwo   = Guid.NewGuid();

        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Doc", null, category.Id, "key/p.pdf", "p.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, acknowledgementDueDate: new DateOnly(2026, 1, 1),
            acknowledgementStatement: null, createdBy: Guid.NewGuid(), now: Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        db.SharedCompanyDocumentAcknowledgements.Add(
            SharedCompanyDocumentAcknowledgement.Create(
                Guid.NewGuid(), companyId, doc.Id, acknowledgedEmployee, 1, "Statement", null, Now));
        await db.SaveChangesAsync();

        var audienceReader = new FakeEmployeeAudienceReader
        {
            EligibleEmployeeIds = [acknowledgedEmployee, overdueEmployeeOne, overdueEmployeeTwo],
        };

        var overdueOnlyResult = await Handler(db, audienceReader).HandleAsync(
            new GetSharedCompanyDocumentAcknowledgementProgressRequest
            {
                CompanyId = companyId, DocumentId = doc.Id, IsOverdue = true,
            },
            CancellationToken.None);

        Assert.True(overdueOnlyResult.IsSuccess);
        Assert.Equal(2, overdueOnlyResult.Value!.Items.Count);
        Assert.All(overdueOnlyResult.Value.Items, i => Assert.Equal("Overdue", i.Status));

        var notOverdueResult = await Handler(db, audienceReader).HandleAsync(
            new GetSharedCompanyDocumentAcknowledgementProgressRequest
            {
                CompanyId = companyId, DocumentId = doc.Id, IsOverdue = false,
            },
            CancellationToken.None);

        Assert.True(notOverdueResult.IsSuccess);
        Assert.Single(notOverdueResult.Value!.Items);
        Assert.Equal(acknowledgedEmployee, notOverdueResult.Value.Items[0].EmployeeId);
    }

    private static GetSharedCompanyDocumentAcknowledgementProgressHandler Handler(
        DocumentsDbContext db,
        FakeEmployeeAudienceReader? audienceReader = null,
        Dictionary<Guid, string>? names = null) =>
        new(db,
            new SharedCompanyDocumentAudienceMatcher(db, audienceReader ?? new FakeEmployeeAudienceReader()),
            audienceReader ?? new FakeEmployeeAudienceReader(),
            new FakeEmployeeNameReader(names),
            new FakeClock(Now.UtcDateTime));

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
