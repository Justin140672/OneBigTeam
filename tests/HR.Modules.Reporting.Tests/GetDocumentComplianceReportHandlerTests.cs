using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetDocumentComplianceReport;
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
}
