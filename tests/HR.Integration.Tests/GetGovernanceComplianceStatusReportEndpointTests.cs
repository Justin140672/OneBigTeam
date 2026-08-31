using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>
/// ADM-08 Governance Compliance Status report (GET). Re-shapes the ADM-02 Compliance Centre data
/// behind the governance policy pair (<c>reporting:view</c> + <c>reporting:view-governance</c>).
/// </summary>
[Collection("Integration")]
public class GetGovernanceComplianceStatusReportEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetGovernanceComplianceStatusReportEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private static string Url(Guid companyId) =>
        $"/api/companies/{companyId}/reporting/governance/compliance-status";

    public static IEnumerable<object[]> ForbiddenRoles() => GovernanceReportingTestContext.ForbiddenRoles();

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url(Guid.NewGuid()))).StatusCode);
    }

    [Theory]
    [MemberData(nameof(ForbiddenRoles))]
    public async Task Returns_Forbidden_For_Non_Governance_Roles(Guid roleId)
    {
        var companyId = Guid.NewGuid();
        using var client = await GovernanceReportingTestContext.ClientForAsync(_factory, companyId, Guid.NewGuid(), roleId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url(companyId))).StatusCode);
    }

    [Fact]
    public async Task Returns_Ok_Empty_Payload_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await GovernanceReportingTestContext.HrAdminClientAsync(_factory, companyId);

        var response = await client.GetAsync(Url(companyId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Payload>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
        Assert.Equal(0, payload.TotalCount);
        Assert.Equal(1, payload.Page);
        Assert.Equal(20, payload.PageSize);
        GovernanceReportingTestContext.AssertNoSensitiveData(await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("?pageSize=0")]
    [InlineData("?pageSize=500")]
    [InlineData("?category=NotACategory")]
    [InlineData("?severity=Critical")]
    [InlineData("?dueDateStart=2026-06-01&dueDateEnd=2026-05-01")]
    public async Task Returns_UnprocessableEntity_For_Invalid_Query(string query)
    {
        var companyId = Guid.NewGuid();
        using var client = await GovernanceReportingTestContext.HrAdminClientAsync(_factory, companyId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync(Url(companyId) + query)).StatusCode);
    }

    [Fact]
    public async Task Accepts_Valid_Category_And_Severity_Filters()
    {
        var companyId = Guid.NewGuid();
        using var client = await GovernanceReportingTestContext.HrAdminClientAsync(_factory, companyId);

        var response = await client.GetAsync(Url(companyId) + "?category=ExpiringVisa&severity=Overdue");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Company_Isolation_Scopes_To_Route_Company()
    {
        var companyB = Guid.NewGuid();
        using var client = await GovernanceReportingTestContext.HrAdminClientAsync(_factory, companyB);
        var payload = await client.GetFromJsonAsync<Payload>(Url(companyB));
        Assert.NotNull(payload);
        Assert.Empty(payload!.Items);
    }

    private sealed record Payload(List<object> Items, int TotalCount, int Page, int PageSize, bool IsTruncated);
}
