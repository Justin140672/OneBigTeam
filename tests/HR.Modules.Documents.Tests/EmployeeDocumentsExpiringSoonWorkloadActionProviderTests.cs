using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;

namespace HR.Modules.Documents.Tests;

/// <summary>
/// OBT-721 workload action provider tests for employee documents expiring soon. HR-only, one
/// summary WorkloadAction per affected employee (see xmldoc on the provider for why a summary
/// rather than one-per-document is used here).
/// </summary>
public class EmployeeDocumentsExpiringSoonWorkloadActionProviderTests
{
    private static ClaimsPrincipal AnyCaller() => new(new ClaimsIdentity());

    private static DocumentComplianceReportItem BuildItem(Guid employeeId, int expiringSoonCount) =>
        new(employeeId, Guid.NewGuid(), 5, 5, 0, expiringSoonCount, 0, []);

    [Fact]
    public async Task HrCaller_Sees_One_Summary_Action_Per_Affected_Employee_CompanyWide()
    {
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();
        var reader = new FakeDocumentComplianceReportReader(
        [
            BuildItem(employeeA, 2),
            BuildItem(employeeB, 1),
        ]);

        var provider = new EmployeeDocumentsExpiringSoonWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task NonHrCaller_Returns_Empty_Not_Throws()
    {
        var reader = new FakeDocumentComplianceReportReader(
        [
            BuildItem(Guid.NewGuid(), 1),
        ]);

        var provider = new EmployeeDocumentsExpiringSoonWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Employees_With_No_Expiring_Documents_Are_Excluded()
    {
        var reader = new FakeDocumentComplianceReportReader(
        [
            BuildItem(Guid.NewGuid(), 0),
        ]);

        var provider = new EmployeeDocumentsExpiringSoonWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(1, "1 document expiring soon")]
    [InlineData(3, "3 documents expiring soon")]
    public async Task Maps_ActionType_Pluralisation_Category_Status_And_DeepLink(int count, string expectedActionType)
    {
        var employeeId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var reader = new FakeDocumentComplianceReportReader(
        [
            BuildItem(employeeId, count),
        ]);

        var provider = new EmployeeDocumentsExpiringSoonWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(expectedActionType, action.ActionType);
        Assert.Equal("Employee Documents Expiring Soon", action.ActionCategory);
        Assert.Equal("Expiring Soon", action.Status);
        Assert.Equal($"/companies/{companyId}/employees/{employeeId}?tab=documents", action.DeepLinkUrl);
    }
}
