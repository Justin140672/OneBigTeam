using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetAssetAssignmentReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetAssetAssignmentReportHandlerTests
{
    private static AssetAssignmentReportItem BuildItem(
        Guid employeeId, string assetName = "AST-001 - Laptop", string? serialNumber = "SN123", string returnStatus = "Assigned") =>
        new(
            Guid.NewGuid(),
            employeeId,
            assetName,
            serialNumber,
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            returnStatus);

    [Fact]
    public async Task HandleAsync_Maps_Multiple_Assignments_To_Rows()
    {
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var reader = new FakeAssetAssignmentReportReader(
            [BuildItem(employeeA, "AST-001 - Laptop"), BuildItem(employeeB, "AST-002 - Monitor", returnStatus: "Returned")]);
        var handler = new GetAssetAssignmentReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(new GetAssetAssignmentReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Items.Count);
        Assert.Equal(2, result.Value.TotalAssignments);
        var rowA = result.Value.Items.Single(r => r.EmployeeId == employeeA);
        Assert.Equal("AST-001 - Laptop", rowA.AssetName);
        var rowB = result.Value.Items.Single(r => r.EmployeeId == employeeB);
        Assert.Equal("Returned", rowB.ReturnStatus);
    }

    [Fact]
    public async Task HandleAsync_Resolves_Employee_Name_Via_Department_Reader()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeAssetAssignmentReportReader([BuildItem(employeeId)]);
        var departments = new Dictionary<Guid, EmployeeDepartmentInfo>
        {
            [employeeId] = new EmployeeDepartmentInfo(employeeId, "Jane Doe", Guid.NewGuid(), "Engineering"),
        };
        var handler = new GetAssetAssignmentReportHandler(reader, new FakeEmployeeDepartmentReader(departments));

        var result = await handler.HandleAsync(new GetAssetAssignmentReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("Jane Doe", row.EmployeeName);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Employee_Id_When_Name_Lookup_Misses()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeAssetAssignmentReportReader([BuildItem(employeeId)]);
        var handler = new GetAssetAssignmentReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(new GetAssetAssignmentReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(employeeId.ToString(), row.EmployeeName);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_Response_When_No_Assignments()
    {
        var reader = new FakeAssetAssignmentReportReader([]);
        var handler = new GetAssetAssignmentReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(new GetAssetAssignmentReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalAssignments);
    }
}
