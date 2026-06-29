using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class PublicHolidayLeaveExclusionTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly Guid UserId = new("cccccccc-0000-0000-0000-000000000001");

    public PublicHolidayLeaveExclusionTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        Task.Run(async () =>
            await TestRoleSeeder.AssignRoleAsync(factory, UserId, SystemRoles.HrAdministrator))
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Submit_LeaveRequest_Excludes_Public_Holiday_When_Setting_Is_Enabled()
    {
        var (client, companyId, leaveTypeId) = await SetupCompanyAsync(excludePublicHolidays: true);

        // Seed a public holiday on Monday 2026-09-07 (within the 5-day Mon–Fri request)
        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays",
            new { companyId, date = "2026-09-07", name = "Test Holiday", countryCode = "GB" });

        var (employeeId, _) = await CreateEmployeeWithPolicyAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests",
            new
            {
                companyId,
                employeeId,
                leaveTypeId,
                startDate = "2026-09-07",
                startPart = "FullDay",
                endDate = "2026-09-11",
                endPart = "FullDay"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestPayload>();
        Assert.Equal(4m, payload!.TotalDays);
    }

    [Fact]
    public async Task Submit_LeaveRequest_Counts_Public_Holiday_When_Setting_Is_Disabled()
    {
        var (client, companyId, leaveTypeId) = await SetupCompanyAsync(excludePublicHolidays: false);

        // Seed a public holiday on Monday 2026-10-05 (within the 5-day Mon–Fri request)
        await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/public-holidays",
            new { companyId, date = "2026-10-05", name = "Test Holiday", countryCode = "GB" });

        var (employeeId, _) = await CreateEmployeeWithPolicyAsync(client, companyId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-requests",
            new
            {
                companyId,
                employeeId,
                leaveTypeId,
                startDate = "2026-10-05",
                startPart = "FullDay",
                endDate = "2026-10-09",
                endPart = "FullDay"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LeaveRequestPayload>();
        Assert.Equal(5m, payload!.TotalDays);
    }

    private HttpClient ClientForCompany(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<(HttpClient Client, Guid CompanyId, Guid LeaveTypeId)> SetupCompanyAsync(bool excludePublicHolidays)
    {
        // Create company — route has no {companyId}, so any tenant is fine here
        var bootstrapClient = _factory.CreateClient();
        bootstrapClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, UserId.ToString());
        bootstrapClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, UserId.ToString());

        var createResp = await bootstrapClient.PostAsJsonAsync("/api/companies", new
        {
            name = $"PH Test {Guid.NewGuid():N}",
            addresses = new[] { new { type = "RegisteredOffice", line1 = "1 Test St", city = "London", countryCode = "GB" } }
        });
        createResp.EnsureSuccessStatusCode();
        var company = await createResp.Content.ReadFromJsonAsync<CompanyPayload>();
        var companyId = company!.Id;

        // All subsequent calls use a client authenticated as this company
        var client = ClientForCompany(companyId);

        // Update settings
        await client.PutAsJsonAsync($"/api/companies/{companyId}/settings", new
        {
            timeZone = "UTC",
            locale = "en-GB",
            workingDays = 31,
            hoursPerDay = 8.0,
            leaveYearStartMonth = 1,
            defaultHolidayAllowance = 25,
            probationMonths = 0,
            excludePublicHolidaysFromLeave = excludePublicHolidays
        });

        // Seed a leave type directly — no API endpoint exists for this
        var leaveTypeId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        db.LeaveTypes.Add(LeaveType.Create(
            leaveTypeId, companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        return (client, companyId, leaveTypeId);
    }

    private async Task<(Guid EmployeeId, Guid PolicyId)> CreateEmployeeWithPolicyAsync(HttpClient client, Guid companyId)
    {
        var policyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = true });
        policyResp.EnsureSuccessStatusCode();
        var policy = await policyResp.Content.ReadFromJsonAsync<PolicyPayload>();

        var empResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new { companyId, firstName = "Test", lastName = "User", workEmail = $"ph.test.{Guid.NewGuid():N}@example.com", startDate = "2026-01-01", dateOfBirth = "1990-01-01", nationality = "British", gender = "Male" });
        empResp.EnsureSuccessStatusCode();
        var employee = await empResp.Content.ReadFromJsonAsync<EmployeePayload>();

        var assignResp = await client.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employee!.Id}/leave-policy",
            new { companyId, employeeId = employee.Id, leavePolicyId = policy!.Id, effectiveFrom = "2026-01-01" });
        assignResp.EnsureSuccessStatusCode();

        return (employee.Id, policy.Id);
    }

    private sealed record CompanyPayload(Guid Id);
    private sealed record PolicyPayload(Guid Id);
    private sealed record EmployeePayload(Guid Id);
    private sealed record LeaveRequestPayload(Guid Id, decimal TotalDays);
}
