using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetEmployeeAcknowledgementHistory;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetEmployeeAcknowledgementHistoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Returns_Items_Ordered_By_AcknowledgedAt_Descending_Without_Throwing()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", Now);
        db.CompanyDocumentCategories.Add(category);

        var docOld = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "Old Doc", null, category.Id, "key/old.pdf", "old.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null,
            Guid.NewGuid(), Now);
        var docNew = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, "New Doc", null, category.Id, "key/new.pdf", "new.pdf", 100, "application/pdf",
            null, null, SharedCompanyDocumentReviewFrequency.None, null, null, requiresAcknowledgement: true, null, null,
            Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.AddRange(docOld, docNew);

        var oldAcknowledgedAt = Now.AddDays(-10);
        var newAcknowledgedAt = Now.AddDays(-1);

        db.SharedCompanyDocumentAcknowledgements.Add(
            SharedCompanyDocumentAcknowledgement.Create(
                Guid.NewGuid(), companyId, docOld.Id, employeeId, 1, "Statement", null, true, oldAcknowledgedAt));
        db.SharedCompanyDocumentAcknowledgements.Add(
            SharedCompanyDocumentAcknowledgement.Create(
                Guid.NewGuid(), companyId, docNew.Id, employeeId, 1, "Statement", null, true, newAcknowledgedAt));
        await db.SaveChangesAsync();

        var result = await Handler(db).HandleAsync(
            new GetEmployeeAcknowledgementHistoryRequest(companyId, employeeId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal("New Doc", result.Value.Items[0].DocumentTitle);
        Assert.Equal(newAcknowledgedAt, result.Value.Items[0].AcknowledgedAt);
        Assert.Equal("Old Doc", result.Value.Items[1].DocumentTitle);
        Assert.Equal(oldAcknowledgedAt, result.Value.Items[1].AcknowledgedAt);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Acknowledgements_Exist()
    {
        await using var db = BuildContext();

        var result = await Handler(db).HandleAsync(
            new GetEmployeeAcknowledgementHistoryRequest(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    private static GetEmployeeAcknowledgementHistoryHandler Handler(DocumentsDbContext db) => new(db);

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
