using HR.Modules.Reporting.Features.AddReportFavourite;
using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Tests;

public class AddReportFavouriteHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 29, 10, 0, 0, DateTimeKind.Utc);

    // "employee-directory" (used throughout this test file) requires reporting:view-hr access in
    // the ReportRegistry.
    private static readonly ReportAccessGates AuthorizedGates = new(
        CanViewRecruitment: false,
        CanViewHr: true,
        CanViewEmployeeStarter: false,
        CanViewLeaveSummary: false,
        CanViewProbation: false,
        CanViewOnboarding: false,
        CanViewWorkloadActions: false,
        CanViewEqualityDiversity: false);

    private static readonly ReportAccessGates UnauthorizedGates = new(
        CanViewRecruitment: false,
        CanViewHr: false,
        CanViewEmployeeStarter: false,
        CanViewLeaveSummary: false,
        CanViewProbation: false,
        CanViewOnboarding: false,
        CanViewWorkloadActions: false,
        CanViewEqualityDiversity: false);

    [Fact]
    public async Task HandleAsync_Fails_Validation_For_Unknown_Report_Id_And_Persists_Nothing()
    {
        await using var db = BuildContext();
        var handler = new AddReportFavouriteHandler(db, new FakeClock(FixedUtcNow));

        var request = new AddReportFavouriteRequest(Guid.NewGuid(), "not-a-real-report");

        var result = await handler.HandleAsync(request, Guid.NewGuid(), AuthorizedGates, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(db.ReportFavourites);
    }

    [Fact]
    public async Task HandleAsync_Fails_Forbidden_For_Unauthorized_Report_And_Persists_Nothing()
    {
        await using var db = BuildContext();
        var handler = new AddReportFavouriteHandler(db, new FakeClock(FixedUtcNow));

        var request = new AddReportFavouriteRequest(Guid.NewGuid(), "employee-directory");

        var result = await handler.HandleAsync(request, Guid.NewGuid(), UnauthorizedGates, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Empty(db.ReportFavourites);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_And_Persists_For_Authorized_Known_Report()
    {
        await using var db = BuildContext();
        var handler = new AddReportFavouriteHandler(db, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var request = new AddReportFavouriteRequest(companyId, "employee-directory");

        var result = await handler.HandleAsync(request, userId, AuthorizedGates, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("employee-directory", result.Value!.ReportId);

        var saved = await db.ReportFavourites.SingleAsync();
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal(userId, saved.UserId);
        Assert.Equal("employee-directory", saved.ReportId);
    }

    [Fact]
    public async Task HandleAsync_Is_Idempotent_When_Called_Twice_For_Same_Report()
    {
        await using var db = BuildContext();
        var handler = new AddReportFavouriteHandler(db, new FakeClock(FixedUtcNow));
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var request = new AddReportFavouriteRequest(companyId, "employee-directory");

        var first = await handler.HandleAsync(request, userId, AuthorizedGates, CancellationToken.None);
        var second = await handler.HandleAsync(request, userId, AuthorizedGates, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Single(db.ReportFavourites);
    }

    private static ReportingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ReportingDbContext(options);
    }
}
