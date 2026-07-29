using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Features.RenameReportView;
using HR.Modules.Reporting.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Tests;

public class RenameReportViewHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Renames_View_When_Owned_By_Caller()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var view = SavedReportView.Create(Guid.NewGuid(), companyId, userId, "employee-directory", "Old Name", "{}", false, FixedNow);
        db.SavedReportViews.Add(view);
        await db.SaveChangesAsync();

        var handler = new RenameReportViewHandler(db);
        var result = await handler.HandleAsync(new RenameReportViewRequest(companyId, view.Id, "New Name"), userId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New Name", result.Value!.Name);

        var reloaded = await db.SavedReportViews.SingleAsync(v => v.Id == view.Id);
        Assert.Equal("New Name", reloaded.Name);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_View_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new RenameReportViewHandler(db);

        var result = await handler.HandleAsync(
            new RenameReportViewRequest(Guid.NewGuid(), Guid.NewGuid(), "New Name"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_View_Not_Owned_By_Caller()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var view = SavedReportView.Create(Guid.NewGuid(), companyId, ownerId, "employee-directory", "Old Name", "{}", false, FixedNow);
        db.SavedReportViews.Add(view);
        await db.SaveChangesAsync();

        var handler = new RenameReportViewHandler(db);
        var result = await handler.HandleAsync(
            new RenameReportViewRequest(companyId, view.Id, "Hijacked Name"), otherUserId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);

        var reloaded = await db.SavedReportViews.SingleAsync(v => v.Id == view.Id);
        Assert.Equal("Old Name", reloaded.Name);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_View_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var userId = Guid.NewGuid();
        var view = SavedReportView.Create(Guid.NewGuid(), Guid.NewGuid(), userId, "employee-directory", "Old Name", "{}", false, FixedNow);
        db.SavedReportViews.Add(view);
        await db.SaveChangesAsync();

        var handler = new RenameReportViewHandler(db);
        var result = await handler.HandleAsync(
            new RenameReportViewRequest(Guid.NewGuid(), view.Id, "New Name"), userId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static ReportingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ReportingDbContext(options);
    }
}
