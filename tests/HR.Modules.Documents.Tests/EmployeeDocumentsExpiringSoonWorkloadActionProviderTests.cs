using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;

namespace HR.Modules.Documents.Tests;

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

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), WorkloadScope.Hr, CancellationToken.None);

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

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), WorkloadScope.Hr, CancellationToken.None);

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

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), WorkloadScope.Hr, CancellationToken.None);

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

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(expectedActionType, action.ActionType);
        Assert.Equal("Employee Documents Expiring Soon", action.ActionCategory);
        Assert.Equal("Expiring Soon", action.Status);
        Assert.Equal($"/companies/{companyId}/employees/{employeeId}?tab=documents", action.DeepLinkUrl);
    }

    [Fact]
    public async Task HrCaller_Requesting_ManagerScope_Returns_Empty_HrOnly_Category_Never_Leaks_Into_Manager_Workspace()
    {
        var reader = new FakeDocumentComplianceReportReader(
        [
            BuildItem(Guid.NewGuid(), 2),
        ]);

        var provider = new EmployeeDocumentsExpiringSoonWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), WorkloadScope.Manager, CancellationToken.None);

        Assert.Empty(result);
    }
}
