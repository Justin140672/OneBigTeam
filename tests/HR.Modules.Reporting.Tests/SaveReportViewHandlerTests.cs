using HR.Modules.Reporting.Features.SaveReportView;
using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Tests;

public class SaveReportViewHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_Creates_SavedReportView()
    {
        await using var db = BuildContext();
        var handler = new SaveReportViewHandler(db, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var request = new SaveReportViewRequest(companyId, "employee-directory", "My View", "{\"a\":1}", false);

        var result = await handler.HandleAsync(request, userId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(Guid.Empty, result.Value!.Id);
        Assert.Equal("employee-directory", result.Value.ReportId);
        Assert.Equal("My View", result.Value.Name);
        Assert.Equal("{\"a\":1}", result.Value.FilterCriteriaJson);
        Assert.False(result.Value.IsDefault);
        Assert.Equal(new DateTimeOffset(FixedUtcNow, TimeSpan.Zero), result.Value.CreatedAt);

        var saved = await db.SavedReportViews.SingleAsync();
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal(userId, saved.UserId);
    }

    [Fact]
    public async Task HandleAsync_Defaults_IsDefault_To_False_When_Not_Specified()
    {
        await using var db = BuildContext();
        var handler = new SaveReportViewHandler(db, new FakeClock(FixedUtcNow));

        var request = new SaveReportViewRequest(Guid.NewGuid(), "employee-directory", "My View", "{}", null);

        var result = await handler.HandleAsync(request, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsDefault);
    }

    [Fact]
    public async Task HandleAsync_Setting_New_Default_Unsets_Previous_Default_For_Same_User_And_Report()
    {
        await using var db = BuildContext();
        var handler = new SaveReportViewHandler(db, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var first = await handler.HandleAsync(
            new SaveReportViewRequest(companyId, "employee-directory", "First", "{}", true), userId, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.True(first.Value!.IsDefault);

        var second = await handler.HandleAsync(
            new SaveReportViewRequest(companyId, "employee-directory", "Second", "{}", true), userId, CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.True(second.Value!.IsDefault);

        var firstReloaded = await db.SavedReportViews.SingleAsync(v => v.Id == first.Value.Id);
        Assert.False(firstReloaded.IsDefault);
    }

    [Fact]
    public async Task HandleAsync_Setting_Default_Does_Not_Affect_Other_Users_Default()
    {
        await using var db = BuildContext();
        var handler = new SaveReportViewHandler(db, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var userAView = await handler.HandleAsync(
            new SaveReportViewRequest(companyId, "employee-directory", "A View", "{}", true), userA, CancellationToken.None);

        var userBView = await handler.HandleAsync(
            new SaveReportViewRequest(companyId, "employee-directory", "B View", "{}", true), userB, CancellationToken.None);

        Assert.True(userAView.IsSuccess);
        Assert.True(userBView.IsSuccess);

        var userAReloaded = await db.SavedReportViews.SingleAsync(v => v.Id == userAView.Value!.Id);
        Assert.True(userAReloaded.IsDefault);
    }

    [Fact]
    public async Task HandleAsync_Setting_Default_Does_Not_Affect_Other_Reports_Default()
    {
        await using var db = BuildContext();
        var handler = new SaveReportViewHandler(db, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var directoryView = await handler.HandleAsync(
            new SaveReportViewRequest(companyId, "employee-directory", "Directory View", "{}", true), userId, CancellationToken.None);

        var sicknessView = await handler.HandleAsync(
            new SaveReportViewRequest(companyId, "sickness-report", "Sickness View", "{}", true), userId, CancellationToken.None);

        Assert.True(directoryView.IsSuccess);
        Assert.True(sicknessView.IsSuccess);

        var directoryReloaded = await db.SavedReportViews.SingleAsync(v => v.Id == directoryView.Value!.Id);
        Assert.True(directoryReloaded.IsDefault);
    }

    private static ReportingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ReportingDbContext(options);
    }
}
