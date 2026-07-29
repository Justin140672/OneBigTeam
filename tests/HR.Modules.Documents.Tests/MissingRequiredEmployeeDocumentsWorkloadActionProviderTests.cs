using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;

namespace HR.Modules.Documents.Tests;

/// <summary>
/// OBT-721 workload action provider tests for employees missing required documents. HR-only, one
/// WorkloadAction per missing document type per employee.
/// </summary>
public class MissingRequiredEmployeeDocumentsWorkloadActionProviderTests
{
    private static ClaimsPrincipal AnyCaller() => new(new ClaimsIdentity());

    private static DocumentComplianceReportItem BuildItem(
        Guid employeeId, int missingCount, params string[] missingDocumentTypeNames) =>
        new(employeeId, Guid.NewGuid(), 5, 5 - missingCount, missingCount, 0, 0, missingDocumentTypeNames);

    [Fact]
    public async Task HrCaller_Sees_One_Action_Per_Missing_Document_Type_Per_Employee()
    {
        var employeeId = Guid.NewGuid();
        var reader = new FakeDocumentComplianceReportReader(
        [
            BuildItem(employeeId, 2, "Passport", "Right to Work Evidence"),
        ]);

        var provider = new MissingRequiredEmployeeDocumentsWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, a => a.ActionType == "Provide Passport");
        Assert.Contains(result, a => a.ActionType == "Provide Right to Work Evidence");
    }

    [Fact]
    public async Task NonHrCaller_Returns_Empty_Not_Throws()
    {
        var reader = new FakeDocumentComplianceReportReader(
        [
            BuildItem(Guid.NewGuid(), 1, "Passport"),
        ]);

        var provider = new MissingRequiredEmployeeDocumentsWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Employees_With_No_Missing_Documents_Are_Excluded()
    {
        var reader = new FakeDocumentComplianceReportReader(
        [
            BuildItem(Guid.NewGuid(), 0),
        ]);

        var provider = new MissingRequiredEmployeeDocumentsWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Maps_ActionCategory_Status_And_DeepLink()
    {
        var employeeId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var reader = new FakeDocumentComplianceReportReader(
        [
            BuildItem(employeeId, 1, "Passport"),
        ]);

        var provider = new MissingRequiredEmployeeDocumentsWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Missing Required Employee Documents", action.ActionCategory);
        Assert.Equal("Missing", action.Status);
        Assert.Null(action.DueDate);
        Assert.Equal($"/companies/{companyId}/employees/{employeeId}/documents", action.DeepLinkUrl);
    }
}
