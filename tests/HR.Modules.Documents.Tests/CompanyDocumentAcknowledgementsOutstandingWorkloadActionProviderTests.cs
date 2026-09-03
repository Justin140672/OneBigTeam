using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;

namespace HR.Modules.Documents.Tests;

/// <summary>
/// OBT-721 workload action provider tests for outstanding company document acknowledgements.
/// HR-only, one WorkloadAction per (document, employee) pair that has not yet acknowledged.
/// </summary>
public class CompanyDocumentAcknowledgementsOutstandingWorkloadActionProviderTests
{
    private static ClaimsPrincipal AnyCaller() => new(new ClaimsIdentity());

    private static CompanyDocumentAcknowledgementReportItem BuildItem(
        Guid sharedDocumentId, string title, Guid employeeId, bool acknowledged) =>
        new(sharedDocumentId, title, employeeId, acknowledged, acknowledged ? DateTimeOffset.UtcNow : null);

    [Fact]
    public async Task HrCaller_Sees_Only_Not_Acknowledged_Items_CompanyWide()
    {
        var docId = Guid.NewGuid();
        var reader = new FakeCompanyDocumentAcknowledgementReportReader(
        [
            BuildItem(docId, "Code of Conduct", Guid.NewGuid(), acknowledged: false),
            BuildItem(docId, "Code of Conduct", Guid.NewGuid(), acknowledged: true),
        ]);

        var provider = new CompanyDocumentAcknowledgementsOutstandingWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task NonHrCaller_Returns_Empty_Not_Throws()
    {
        var reader = new FakeCompanyDocumentAcknowledgementReportReader(
        [
            BuildItem(Guid.NewGuid(), "Code of Conduct", Guid.NewGuid(), acknowledged: false),
        ]);

        var provider = new CompanyDocumentAcknowledgementsOutstandingWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService());

        var result = await provider.GetActionsAsync(Guid.NewGuid(), AnyCaller(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Maps_ActionType_Category_Status_And_DeepLink()
    {
        var docId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var reader = new FakeCompanyDocumentAcknowledgementReportReader(
        [
            BuildItem(docId, "Code of Conduct", employeeId, acknowledged: false),
        ]);

        var provider = new CompanyDocumentAcknowledgementsOutstandingWorkloadActionProvider(
            reader, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"));

        var result = await provider.GetActionsAsync(companyId, AnyCaller(), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Acknowledge \"Code of Conduct\"", action.ActionType);
        Assert.Equal("Company Document Acknowledgements Outstanding", action.ActionCategory);
        Assert.Equal("Not Acknowledged", action.Status);
        Assert.Null(action.DueDate);
        Assert.Equal($"/companies/{companyId}/shared-documents/{docId}/acknowledgement-progress", action.DeepLinkUrl);
    }
}
