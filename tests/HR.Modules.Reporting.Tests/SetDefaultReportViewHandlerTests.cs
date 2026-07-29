using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Features.SetDefaultReportView;
using HR.Modules.Reporting.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Tests;

public class SetDefaultReportViewHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Sets_View_As_Default_When_Owned_By_Caller()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var view = SavedReportView.Create(Guid.NewGuid(), companyId, userId, "employee-directory", "My View", "{}", false, FixedNow);
        db.SavedReportViews.Add(view);
        await db.SaveChangesAsync();

        var handler = new SetDefaultReportViewHandler(db);
        var result = await handler.HandleAsync(new SetDefaultReportViewRequest(companyId, view.Id), userId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsDefault);

        var reloaded = await db.SavedReportViews.SingleAsync(v => v.Id == view.Id);
        Assert.True(reloaded.IsDefault);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_View_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new SetDefaultReportViewHandler(db);

        var result = await handler.HandleAsync(
            new SetDefaultReportViewRequest(Guid.NewGuid(), Guid.NewGuid()), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_View_Not_Owned_By_Caller_And_Leaves_Default_Unchanged()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var ownerDefault = SavedReportView.Create(Guid.NewGuid(), companyId, ownerId, "employee-directory", "Owner Default", "{}", true, FixedNow);
        var otherView = SavedReportView.Create(Guid.NewGuid(), companyId, ownerId, "employee-directory", "Not Default", "{}", false, FixedNow);
        db.SavedReportViews.AddRange(ownerDefault, otherView);
        await db.SaveChangesAsync();

        var handler = new SetDefaultReportViewHandler(db);
        var result = await handler.HandleAsync(new SetDefaultReportViewRequest(companyId, otherView.Id), otherUserId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);

        var reloadedOwnerDefault = await db.SavedReportViews.SingleAsync(v => v.Id == ownerDefault.Id);
        Assert.True(reloadedOwnerDefault.IsDefault);
        var reloadedOtherView = await db.SavedReportViews.SingleAsync(v => v.Id == otherView.Id);
        Assert.False(reloadedOtherView.IsDefault);
    }

    [Fact]
    public async Task HandleAsync_Setting_New_Default_Unsets_Previous_Default_For_Same_User_And_Report_Only()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var firstView = SavedReportView.Create(Guid.NewGuid(), companyId, userId, "employee-directory", "First", "{}", true, FixedNow);
        var secondView = SavedReportView.Create(Guid.NewGuid(), companyId, userId, "employee-directory", "Second", "{}", false, FixedNow);
        var otherReportDefault = SavedReportView.Create(Guid.NewGuid(), companyId, userId, "sickness-report", "Sickness Default", "{}", true, FixedNow);
        var otherUserDefault = SavedReportView.Create(Guid.NewGuid(), companyId, otherUserId, "employee-directory", "Other User Default", "{}", true, FixedNow);
        db.SavedReportViews.AddRange(firstView, secondView, otherReportDefault, otherUserDefault);
        await db.SaveChangesAsync();

        var handler = new SetDefaultReportViewHandler(db);
        var result = await handler.HandleAsync(new SetDefaultReportViewRequest(companyId, secondView.Id), userId, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var reloadedFirst = await db.SavedReportViews.SingleAsync(v => v.Id == firstView.Id);
        var reloadedSecond = await db.SavedReportViews.SingleAsync(v => v.Id == secondView.Id);
        var reloadedOtherReport = await db.SavedReportViews.SingleAsync(v => v.Id == otherReportDefault.Id);
        var reloadedOtherUser = await db.SavedReportViews.SingleAsync(v => v.Id == otherUserDefault.Id);

        Assert.False(reloadedFirst.IsDefault);
        Assert.True(reloadedSecond.IsDefault);
        Assert.True(reloadedOtherReport.IsDefault);
        Assert.True(reloadedOtherUser.IsDefault);
    }

    private static ReportingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ReportingDbContext(options);
    }
}
