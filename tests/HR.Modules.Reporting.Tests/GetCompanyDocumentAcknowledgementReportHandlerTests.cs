using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.GetCompanyDocumentAcknowledgementReport;
using HR.Modules.Reporting.Tests.Infrastructure;

namespace HR.Modules.Reporting.Tests;

public class GetCompanyDocumentAcknowledgementReportHandlerTests
{
    private static CompanyDocumentAcknowledgementReportItem BuildItem(
        Guid employeeId, bool acknowledged, DateTimeOffset? acknowledgedAt = null) =>
        new(Guid.NewGuid(), "Employee Handbook", employeeId, acknowledged, acknowledgedAt);

    [Fact]
    public async Task HandleAsync_Computes_Acknowledged_And_Outstanding_Counts()
    {
        var reader = new FakeCompanyDocumentAcknowledgementReportReader(
        [
            BuildItem(Guid.NewGuid(), acknowledged: true, DateTimeOffset.UtcNow),
            BuildItem(Guid.NewGuid(), acknowledged: true, DateTimeOffset.UtcNow),
            BuildItem(Guid.NewGuid(), acknowledged: false),
        ]);
        var handler = new GetCompanyDocumentAcknowledgementReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(
            new GetCompanyDocumentAcknowledgementReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        Assert.Equal(3, response.TotalRequired);
        Assert.Equal(2, response.TotalAcknowledged);
        Assert.Equal(1, response.TotalOutstanding);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_Response_When_No_Items()
    {
        var reader = new FakeCompanyDocumentAcknowledgementReportReader([]);
        var handler = new GetCompanyDocumentAcknowledgementReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(
            new GetCompanyDocumentAcknowledgementReportRequest(Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        Assert.Equal(0, result.Value.TotalRequired);
    }

    [Fact]
    public async Task HandleAsync_Rows_Carry_DocumentTitle_And_AcknowledgedAt()
    {
        var employeeId = Guid.NewGuid();
        var acknowledgedAt = DateTimeOffset.UtcNow;
        var reader = new FakeCompanyDocumentAcknowledgementReportReader(
        [
            BuildItem(employeeId, acknowledged: true, acknowledgedAt),
        ]);
        var handler = new GetCompanyDocumentAcknowledgementReportHandler(reader, new FakeEmployeeDepartmentReader());

        var result = await handler.HandleAsync(
            new GetCompanyDocumentAcknowledgementReportRequest(Guid.NewGuid()), CancellationToken.None);

        var row = Assert.Single(result.Value!.Items);
        Assert.Equal("Employee Handbook", row.DocumentTitle);
        Assert.True(row.Acknowledged);
        Assert.Equal(acknowledgedAt, row.AcknowledgedAt);
    }
}
