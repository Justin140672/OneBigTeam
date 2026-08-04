using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services.OnboardingTasks;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class ReviewCompanyDocumentsTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_No_Documents_Exist()
    {
        await using var context = BuildContext();

        var task = new ReviewCompanyDocumentsTask(context);

        var result = await task.IsCompletedAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_False_When_Documents_Exist_But_None_Published()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.SharedCompanyDocuments.Add(CreateDoc(companyId));
        await context.SaveChangesAsync();

        var task = new ReviewCompanyDocumentsTask(context);

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsCompletedAsync_Returns_True_When_At_Least_One_Document_Published()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var doc = CreateDoc(companyId);
        doc.Publish(Guid.NewGuid(), Now.AddDays(1));
        context.SharedCompanyDocuments.Add(doc);
        await context.SaveChangesAsync();

        var task = new ReviewCompanyDocumentsTask(context);

        var result = await task.IsCompletedAsync(companyId, CancellationToken.None);

        Assert.True(result);
    }

    private static SharedCompanyDocument CreateDoc(Guid companyId)
    {
        return SharedCompanyDocument.Create(
            Guid.NewGuid(),
            companyId,
            "Employee Handbook",
            null,
            Guid.NewGuid(),
            "documents/handbook.pdf",
            "handbook.pdf",
            1024,
            "application/pdf",
            effectiveDate: null,
            reviewDate: null,
            SharedCompanyDocumentReviewFrequency.Yearly,
            customReviewFrequencyMonths: null,
            reviewOwnerEmployeeId: null,
            requiresAcknowledgement: false,
            acknowledgementDueDate: null,
            acknowledgementStatement: null,
            createdBy: Guid.NewGuid(),
            Now);
    }

    private static DocumentsDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new DocumentsDbContext(options);
    }
}
