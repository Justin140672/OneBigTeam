using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetDocumentComplianceReport;
using HR.Modules.Reporting.ReportRegistry;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetDocumentComplianceReportHandlerTests
{
    private static DocumentComplianceReportItem BuildItem(
        Guid employeeId,
        Guid? positionProfileId = null,
        int missing = 1,
        int expiringSoon = 1,
        int expired = 1) =>
        new(
            employeeId,
            positionProfileId,
            RequiredCount: 5,
            UploadedCount: 2,
            missing,
            expiringSoon,
            expired,
            MissingDocumentTypeNames: ["Passport"]);

    [Fact]
    public async Task HandleAsync_PositionProfileId_Filter_Narrows_Results()
    {
        var profileA = Guid.NewGuid();
        var profileB = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var reader = new FakeDocumentComplianceReportReader(
        [
            BuildItem(employeeA, profileA),
            BuildItem(employeeB, profileB),
        ]);
        var handler = new GetDocumentComplianceReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(
            new GetDocumentComplianceReportRequest(Guid.NewGuid(), profileA), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(employeeA, row.EmployeeId);
        Assert.Equal(profileA, reader.LastPositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_No_Filter_Returns_All_Employees()
    {
        var reader = new FakeDocumentComplianceReportReader(
        [
            BuildItem(Guid.NewGuid()),
            BuildItem(Guid.NewGuid()),
        ]);
        var handler = new GetDocumentComplianceReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(
            new GetDocumentComplianceReportRequest(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Null(reader.LastPositionProfileId);
    }

    [Fact]
    public async Task HandleAsync_Computes_Missing_ExpiringSoon_Expired_Totals()
    {
        var reader = new FakeDocumentComplianceReportReader(
        [
            BuildItem(Guid.NewGuid(), missing: 2, expiringSoon: 1, expired: 0),
            BuildItem(Guid.NewGuid(), missing: 1, expiringSoon: 3, expired: 2),
        ]);
        var handler = new GetDocumentComplianceReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(
            new GetDocumentComplianceReportRequest(Guid.NewGuid(), null), CancellationToken.None);

        var response = result.Value!;
        Assert.Equal(2, response.TotalEmployees);
        Assert.Equal(3, response.TotalMissing);
        Assert.Equal(4, response.TotalExpiringSoon);
        Assert.Equal(2, response.TotalExpired);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_Response_When_No_Items()
    {
        var reader = new FakeDocumentComplianceReportReader([]);
        var handler = new GetDocumentComplianceReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(
            new GetDocumentComplianceReportRequest(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalMissing);
    }

    [Fact]
    public async Task HandleAsync_Below_DisplayRowLimit_Is_Not_Truncated()
    {
        var reader = new FakeDocumentComplianceReportReader(
        [
            BuildItem(Guid.NewGuid()),
            BuildItem(Guid.NewGuid()),
        ]);
        var handler = new GetDocumentComplianceReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(
            new GetDocumentComplianceReportRequest(Guid.NewGuid(), null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsTruncated);
        Assert.Equal(2, result.Value.TotalEmployees);
        Assert.Equal(2, result.Value.Items.Count);
    }

    [Fact]
    public async Task HandleAsync_Above_DisplayRowLimit_Is_Truncated_And_Sums_Computed_From_Full_Set()
    {
        // Seed limit+500 items, each contributing 1 to missing/expiringSoon/expired, so the
        // full-set sum (limit+500) is clearly distinguishable from the capped-set sum (limit).
        const int overLimitBy = 500;
        var totalItems = ReportLimits.DisplayRowLimit + overLimitBy;
        var items = Enumerable.Range(0, totalItems)
            .Select(_ => BuildItem(Guid.NewGuid(), missing: 1, expiringSoon: 1, expired: 1))
            .ToList();
        var reader = new FakeDocumentComplianceReportReader(items);
        var handler = new GetDocumentComplianceReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(
            new GetDocumentComplianceReportRequest(Guid.NewGuid(), null), CancellationToken.None);

        var response = result.Value!;
        Assert.True(response.IsTruncated);
        Assert.Equal(totalItems, response.TotalEmployees);
        Assert.Equal(ReportLimits.DisplayRowLimit, response.Items.Count);
        // Sums must reflect the FULL set (totalItems), not just the capped rows (DisplayRowLimit).
        Assert.Equal(totalItems, response.TotalMissing);
        Assert.Equal(totalItems, response.TotalExpiringSoon);
        Assert.Equal(totalItems, response.TotalExpired);
    }
}
