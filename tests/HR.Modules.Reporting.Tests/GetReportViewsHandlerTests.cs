using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Features.GetReportViews;
using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.ReportRegistry;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Tests;

public class GetReportViewsHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    // "employee-directory" and "sickness-report" (used throughout this test file) both require
    // reporting:view-hr access in the ReportRegistry.
    private static readonly ReportAccessGates AuthorizedGates = new(
        CanViewRecruitment: false,
        CanViewHr: true,
        CanViewEmployeeStarter: false,
        CanViewLeaveSummary: false,
        CanViewProbation: false,
        CanViewOnboarding: false,
        CanViewWorkloadActions: false,
        CanViewEqualityDiversity: false);

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_No_Views_Saved()
    {
        await using var db = BuildContext();
        var handler = new GetReportViewsHandler(db);

        var result = await handler.HandleAsync(
            new GetReportViewsRequest(Guid.NewGuid(), "employee-directory"), Guid.NewGuid(), AuthorizedGates, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Views);
    }

    [Fact]
    public async Task HandleAsync_Returns_Only_Caller_Views_For_Requested_Report()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        var mine = SavedReportView.Create(Guid.NewGuid(), companyId, userId, "employee-directory", "Mine", "{}", false, FixedNow);
        var otherReport = SavedReportView.Create(Guid.NewGuid(), companyId, userId, "sickness-report", "Other Report", "{}", false, FixedNow);
        var otherUser = SavedReportView.Create(Guid.NewGuid(), companyId, otherUserId, "employee-directory", "Other User", "{}", false, FixedNow);
        db.SavedReportViews.AddRange(mine, otherReport, otherUser);
        await db.SaveChangesAsync();

        var handler = new GetReportViewsHandler(db);
        var result = await handler.HandleAsync(
            new GetReportViewsRequest(companyId, "employee-directory"), userId, AuthorizedGates, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Views);
        Assert.Equal(mine.Id, item.Id);
    }

    [Fact]
    public async Task HandleAsync_Orders_Default_First_Then_By_Name()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var zebra = SavedReportView.Create(Guid.NewGuid(), companyId, userId, "employee-directory", "Zebra", "{}", false, FixedNow);
        var apple = SavedReportView.Create(Guid.NewGuid(), companyId, userId, "employee-directory", "Apple", "{}", false, FixedNow);
        var defaultView = SavedReportView.Create(Guid.NewGuid(), companyId, userId, "employee-directory", "Middle Default", "{}", true, FixedNow);
        db.SavedReportViews.AddRange(zebra, apple, defaultView);
        await db.SaveChangesAsync();

        var handler = new GetReportViewsHandler(db);
        var result = await handler.HandleAsync(
            new GetReportViewsRequest(companyId, "employee-directory"), userId, AuthorizedGates, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var views = result.Value!.Views;
        Assert.Equal(3, views.Count);
        Assert.Equal(defaultView.Id, views[0].Id);
        Assert.True(views[0].IsDefault);
        Assert.Equal(apple.Id, views[1].Id);
        Assert.Equal(zebra.Id, views[2].Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_Caller_Not_Authorized_For_Report_Even_If_Rows_Exist()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var view = SavedReportView.Create(Guid.NewGuid(), companyId, userId, "employee-directory", "Mine", "{}", false, FixedNow);
        db.SavedReportViews.Add(view);
        await db.SaveChangesAsync();

        var unauthorizedGates = new ReportAccessGates(
            CanViewRecruitment: false,
            CanViewHr: false,
            CanViewEmployeeStarter: false,
            CanViewLeaveSummary: false,
            CanViewProbation: false,
            CanViewOnboarding: false,
            CanViewWorkloadActions: false,
            CanViewEqualityDiversity: false);

        var handler = new GetReportViewsHandler(db);
        var result = await handler.HandleAsync(
            new GetReportViewsRequest(companyId, "employee-directory"), userId, unauthorizedGates, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Views);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_Report_Id_Is_Not_In_Catalogue()
    {
        await using var db = BuildContext();
        var handler = new GetReportViewsHandler(db);

        var result = await handler.HandleAsync(
            new GetReportViewsRequest(Guid.NewGuid(), "not-a-real-report"), Guid.NewGuid(), AuthorizedGates, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Views);
    }

    private static ReportingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ReportingDbContext(options);
    }
}
