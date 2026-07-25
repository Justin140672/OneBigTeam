using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

public class GetOrganisationChartEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid AdminUserId = new("f0000c48-0000-0000-0000-000000000001");

    public GetOrganisationChartEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, AdminUserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Get_OrganisationChart_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/companies/{Guid.NewGuid()}/organisation-chart");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_OrganisationChart_Is_Company_Scoped_And_Excludes_Inactive_And_Terminated_Employees()
    {
        var companyId = Guid.NewGuid();
        using var client = AdminClient(companyId);

        var (departmentId, locationId, positionProfileId, employmentTypeId) = await CreateReferenceDataAsync(client, companyId);

        var managerId = await CreateEmployeeAsync(
            client, companyId, "Mia", "Manager", departmentId, locationId, positionProfileId, employmentTypeId, "ORG-MGR");
        var reportId = await CreateEmployeeAsync(
            client, companyId, "Rick", "Report", departmentId, locationId, positionProfileId, employmentTypeId, "ORG-REP", managerId);

        // A Draft employee (never activated) — should be excluded as "inactive".
        await CreateEmployeeAsync(
            client, companyId, "Dana", "Draft", departmentId, locationId, positionProfileId, employmentTypeId, "ORG-DRAFT",
            activate: false);

        // An employee in a completely different company — should never appear.
        var otherCompanyId = Guid.NewGuid();
        using var otherClient = AdminClient(otherCompanyId);
        var (otherDeptId, otherLocId, otherProfileId, otherTypeId) = await CreateReferenceDataAsync(otherClient, otherCompanyId);
        await CreateEmployeeAsync(
            otherClient, otherCompanyId, "Olivia", "Other", otherDeptId, otherLocId, otherProfileId, otherTypeId, "ORG-OTHER");

        // Status is an optional filter on this endpoint (defaults to showing every status) —
        // ?status=Active mirrors what the Organisation Chart page itself defaults to, and is
        // needed here so the never-activated Draft employee below is excluded as intended.
        var response = await client.GetAsync($"/api/companies/{companyId}/organisation-chart?status=Active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OrganisationChartPayload>();
        Assert.NotNull(payload);

        // Only the two Active employees for this company — Draft and the other company's
        // employee are both excluded.
        Assert.Equal(2, payload!.Items.Count);

        var manager = Assert.Single(payload.Items, i => i.EmployeeId == managerId);
        var report = Assert.Single(payload.Items, i => i.EmployeeId == reportId);

        Assert.Equal("Mia Manager", manager.Name);
        Assert.Equal("Senior Software Engineer", manager.JobTitle);
        Assert.Equal("Engineering", manager.Department);
        Assert.Equal("London Office", manager.Location);
        Assert.Null(manager.ManagerId);

        Assert.Equal("Rick Report", report.Name);
        Assert.Equal(managerId, report.ManagerId);
    }

    private HttpClient AdminClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, AdminUserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private static async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)> CreateReferenceDataAsync(
        HttpClient client, Guid companyId)
    {
        var departmentId = await PostForIdAsync(client, $"/api/companies/{companyId}/departments",
            new { companyId, name = "Engineering" });

        var locationTypeId = await PostForIdAsync(client, $"/api/companies/{companyId}/location-types",
            new { companyId, name = "Office" });

        var locationId = await PostForIdAsync(client, $"/api/companies/{companyId}/locations",
            new { companyId, name = "London Office", locationTypeId });

        var leavePolicyId = await PostForIdAsync(client, $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = "Standard", carryOverDays = 0, allowNegativeBalance = false });

        var positionProfileId = await PostForIdAsync(client, $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = "Senior Software Engineer", defaultLeavePolicyId = leavePolicyId });

        var employmentTypeId = await PostForIdAsync(client, $"/api/companies/{companyId}/employment-types",
            new { companyId, name = "Permanent" });

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    // Employees are created in Draft status (see Employee.Create/CreateEmployeeHandler — nothing
    // auto-activates them). The Employment tab's own PUT is how HR actually activates an
    // employee in this system (UpdateEmploymentDetailsValidator rejects Status == Draft, so
    // there's no separate "Activate" endpoint) — it also doubles as where a manager gets
    // assigned, so both happen in one follow-up call here.
    private static async Task<Guid> CreateEmployeeAsync(
        HttpClient client, Guid companyId, string firstName, string lastName,
        Guid departmentId, Guid locationId, Guid positionProfileId, Guid employmentTypeId,
        string employeeNumber, Guid? managerId = null, bool activate = true)
    {
        var createResponse = await client.PostAsJsonAsync($"/api/companies/{companyId}/employees", new
        {
            companyId,
            firstName,
            lastName,
            workEmail = $"{firstName}.{lastName}.{Guid.NewGuid():N}@orgcharttest.example",
            startDate = "2026-01-01",
            dateOfBirth = "1990-01-01",
            nationality = "British",
            gender = "Prefer not to say",
            employeeNumber,
            employmentTypeId,
            departmentId,
            locationId,
            positionProfileId
        });
        createResponse.EnsureSuccessStatusCode();
        var employeeId = (await createResponse.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        if (activate)
        {
            var employmentResponse = await client.PutAsJsonAsync(
                $"/api/companies/{companyId}/employees/{employeeId}/employment",
                new
                {
                    companyId,
                    id = employeeId,
                    employeeNumber,
                    employmentTypeId,
                    status = "Active",
                    departmentId,
                    locationId,
                    positionProfileId,
                    managerId,
                    startDate = "2026-01-01"
                });
            employmentResponse.EnsureSuccessStatusCode();
        }

        return employeeId;
    }

    private static async Task<Guid> PostForIdAsync(HttpClient client, string url, object payload)
    {
        var response = await client.PostAsJsonAsync(url, payload);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
    }

    private sealed record IdPayload(Guid Id);

    private sealed record OrganisationChartItemPayload(
        Guid EmployeeId, string Name, string JobTitle, string Department, Guid? ManagerId,
        string Location, string? ProfilePhotoUrl);

    private sealed record OrganisationChartPayload(List<OrganisationChartItemPayload> Items);
}
