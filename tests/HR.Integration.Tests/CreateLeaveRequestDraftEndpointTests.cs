using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;

namespace HR.Integration.Tests;

[Collection("Integration")]
public class CreateLeaveRequestDraftEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid User1 = new("cccccccc-0000-0000-0000-000000000001");

    // Pre-seeded leave type for the seeded company (see LeaveModule.SeedLeaveAsync)
    private static readonly Guid SeededCompanyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid AnnualLeaveTypeId = Guid.Parse("A0000000-0000-0000-0000-000000000001");

    public CreateLeaveRequestDraftEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, User1, SystemRoles.HrAdministrator);
        }).GetAwaiter().GetResult();
    }

    private async Task<(HttpClient Client, Guid EmployeeId)> SetupEmployeeAsync()
    {
        var client = _factory.CreateClient();
        var companyId = SeededCompanyId;

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, User1.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, User1, SystemRoles.HrAdministrator, companyId);

        var employeeResponse = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                companyId,
                firstName = "Draft",
                lastName = "Tester",
                workEmail = $"draft.tester.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"DT-{Guid.NewGuid():N}",
                employmentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001"),
                departmentId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
                locationId = Guid.Parse("70000000-0000-0000-0000-000000000001"),
                positionProfileId = Guid.Parse("20000000-0000-0000-0000-000000000002")
            });
        employeeResponse.EnsureSuccessStatusCode();
        var employee = await employeeResponse.Content.ReadFromJsonAsync<EmployeePayload>();

        return (client, employee!.Id);
    }

    [Fact]
    public async Task Post_Draft_Returns_Created_With_Draft_Status()
    {
        var (client, employeeId) = await SetupEmployeeAsync();
        var companyId = SeededCompanyId;

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/drafts",
            new
            {
                companyId,
                employeeId,
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-08-03",
                startPart = "FullDay",
                endDate = "2026-08-07",
                endPart = "FullDay",
                reason = "Draft holiday"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestDraftPayload>();
        Assert.NotNull(payload);
        Assert.Equal("Draft", payload!.Status);
        Assert.Equal(employeeId, payload.EmployeeId);
    }

    [Fact]
    public async Task Post_Draft_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var companyId = SeededCompanyId;
        var employeeId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/drafts",
            new
            {
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-08-03",
                startPart = "FullDay",
                endDate = "2026-08-07",
                endPart = "FullDay"
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Draft_Returns_BadRequest_When_EndDate_Before_StartDate()
    {
        var (client, employeeId) = await SetupEmployeeAsync();
        var companyId = SeededCompanyId;

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests/drafts",
            new
            {
                companyId,
                employeeId,
                leaveTypeId = AnnualLeaveTypeId,
                startDate = "2026-08-07",
                startPart = "FullDay",
                endDate = "2026-08-03",
                endPart = "FullDay"
            });

        // FluentValidation failures are intercepted by the FastEndpoints pipeline before the
        // handler runs and return 422, not 400 - matches this codebase's other validator-failure
        // integration tests for the Leave module's request-body validators.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    private sealed record EmployeePayload(Guid Id, Guid CompanyId, string Status);
    private sealed record LeaveRequestDraftPayload(Guid Id, Guid CompanyId, Guid EmployeeId, string Status);
}
