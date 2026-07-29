using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Features.DeleteReportView;
using HR.Modules.Reporting.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Tests;

public class DeleteReportViewHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Deletes_View_When_Owned_By_Caller()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var view = SavedReportView.Create(Guid.NewGuid(), companyId, userId, "employee-directory", "My View", "{}", false, FixedNow);
        db.SavedReportViews.Add(view);
        await db.SaveChangesAsync();

        var handler = new DeleteReportViewHandler(db);
        var result = await handler.HandleAsync(new DeleteReportViewRequest(companyId, view.Id), userId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(view.Id, result.Value!.Id);
        Assert.False(await db.SavedReportViews.AnyAsync(v => v.Id == view.Id));
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_View_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new DeleteReportViewHandler(db);

        var result = await handler.HandleAsync(
            new DeleteReportViewRequest(Guid.NewGuid(), Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_View_Not_Owned_By_Caller_And_Leaves_View_Intact()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var view = SavedReportView.Create(Guid.NewGuid(), companyId, ownerId, "employee-directory", "My View", "{}", false, FixedNow);
        db.SavedReportViews.Add(view);
        await db.SaveChangesAsync();

        var handler = new DeleteReportViewHandler(db);
        var result = await handler.HandleAsync(new DeleteReportViewRequest(companyId, view.Id), otherUserId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.True(await db.SavedReportViews.AnyAsync(v => v.Id == view.Id));
    }

    private static ReportingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ReportingDbContext(options);
    }
}
