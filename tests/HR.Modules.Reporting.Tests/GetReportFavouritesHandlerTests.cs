using HR.Modules.Reporting.Domain;
using HR.Modules.Reporting.Features.GetReportFavourites;
using HR.Modules.Reporting.Persistence;
using HR.Modules.Reporting.ReportRegistry;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Reporting.Tests;

public class GetReportFavouritesHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 29, 10, 0, 0, TimeSpan.Zero);

    private static readonly ReportAccessGates HrOnlyGates = new(
        CanViewRecruitment: false,
        CanViewHr: true,
        CanViewEmployeeStarter: false,
        CanViewLeaveSummary: false,
        CanViewProbation: false,
        CanViewOnboarding: false,
        CanViewWorkloadActions: false,
        CanViewEqualityDiversity: false);

    private static readonly ReportAccessGates NoAccessGates = new(
        CanViewRecruitment: false,
        CanViewHr: false,
        CanViewEmployeeStarter: false,
        CanViewLeaveSummary: false,
        CanViewProbation: false,
        CanViewOnboarding: false,
        CanViewWorkloadActions: false,
        CanViewEqualityDiversity: false);

    [Fact]
    public async Task HandleAsync_Returns_Empty_When_None_Favourited()
    {
        await using var db = BuildContext();
        var handler = new GetReportFavouritesHandler(db);

        var result = await handler.HandleAsync(
            new GetReportFavouritesRequest(Guid.NewGuid()), Guid.NewGuid(), HrOnlyGates, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.ReportIds);
    }

    [Fact]
    public async Task HandleAsync_Omits_Favourite_When_Caller_No_Longer_Authorized_For_Its_Report()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.ReportFavourites.Add(ReportFavourite.Create(Guid.NewGuid(), companyId, userId, "employee-directory", FixedNow));
        await db.SaveChangesAsync();

        var handler = new GetReportFavouritesHandler(db);

        // Simulates a permission revoked after the favourite was saved: "employee-directory"
        // requires reporting:view-hr, which NoAccessGates does not grant.
        var result = await handler.HandleAsync(
            new GetReportFavouritesRequest(companyId), userId, NoAccessGates, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.ReportIds);
    }

    [Fact]
    public async Task HandleAsync_Omits_Favourite_For_Report_No_Longer_In_Catalogue()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.ReportFavourites.Add(ReportFavourite.Create(Guid.NewGuid(), companyId, userId, "retired-report", FixedNow));
        await db.SaveChangesAsync();

        var handler = new GetReportFavouritesHandler(db);

        // Even with every gate granted, a favourite for a report id that isn't in the catalogue at
        // all must still be silently omitted rather than erroring.
        var fullAccessGates = new ReportAccessGates(true, true, true, true, true, true, true, true);

        var result = await handler.HandleAsync(
            new GetReportFavouritesRequest(companyId), userId, fullAccessGates, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.ReportIds);
    }

    [Fact]
    public async Task HandleAsync_Returns_Favourite_When_Still_Authorized_And_In_Catalogue()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.ReportFavourites.Add(ReportFavourite.Create(Guid.NewGuid(), companyId, userId, "employee-directory", FixedNow));
        await db.SaveChangesAsync();

        var handler = new GetReportFavouritesHandler(db);

        var result = await handler.HandleAsync(
            new GetReportFavouritesRequest(companyId), userId, HrOnlyGates, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.ReportIds, id => id == "employee-directory");
    }

    private static ReportingDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ReportingDbContext(options);
    }
}
