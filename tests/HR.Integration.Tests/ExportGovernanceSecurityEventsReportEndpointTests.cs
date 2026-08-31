using System.Net;
using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests;

/// <summary>ADM-08 Governance Security Events report export. Same policy pair as its GET endpoint.</summary>
[Collection("Integration")]
public class ExportGovernanceSecurityEventsReportEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public ExportGovernanceSecurityEventsReportEndpointTests(ApiWebApplicationFactory factory) => _factory = factory;

    private static string Url(Guid companyId) =>
        $"/api/companies/{companyId}/reporting/governance/security-events/export";

    public static IEnumerable<object[]> ForbiddenRoles() => GovernanceReportingTestContext.ForbiddenRoles();

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Url(Guid.NewGuid()) + "?format=Csv")).StatusCode);
    }

    [Theory]
    [MemberData(nameof(ForbiddenRoles))]
    public async Task Returns_Forbidden_For_The_Same_Callers_The_Get_Endpoint_Rejects(Guid roleId)
    {
        var companyId = Guid.NewGuid();
        using var client = await GovernanceReportingTestContext.ClientForAsync(_factory, companyId, Guid.NewGuid(), roleId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url(companyId) + "?format=Csv")).StatusCode);
    }

    [Fact]
    public async Task Returns_Csv_For_HrAdministrator()
    {
        var companyId = Guid.NewGuid();
        using var client = await GovernanceReportingTestContext.HrAdminClientAsync(_factory, companyId);

        var response = await client.GetAsync(Url(companyId) + "?format=Csv");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("Occurred At (UTC),Event Type,Entity Type,Actor,Status,Summary", body);
        GovernanceReportingTestContext.AssertNoSensitiveData(body);
    }

    [Theory]
    [InlineData("?format=Csv&status=bogus")]
    [InlineData("?format=Csv&fromDate=2026-06-30&toDate=2026-06-01")]
    public async Task Returns_UnprocessableEntity_For_Invalid_Query(string query)
    {
        var companyId = Guid.NewGuid();
        using var client = await GovernanceReportingTestContext.HrAdminClientAsync(_factory, companyId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.GetAsync(Url(companyId) + query)).StatusCode);
    }
}
