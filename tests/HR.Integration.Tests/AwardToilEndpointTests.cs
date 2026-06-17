using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

public class AwardToilEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid HrAdminUserId  = new("cccccccc-aaaa-0000-0000-000000000001");
    private static readonly Guid ManagerUserId  = new("cccccccc-aaaa-0000-0000-000000000002");
    private static readonly Guid EmployeeUserId = new("cccccccc-aaaa-0000-0000-000000000003");

    public AwardToilEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUserId, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, ManagerUserId, SystemRoles.Manager);
            await TestRoleSeeder.AssignRoleAsync(factory, EmployeeUserId, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Post_AwardToil_Returns_Unauthorized_For_Anonymous_Request()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/employees/{Guid.NewGuid()}/toil",
            new { });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_AwardToil_Returns_Forbidden_For_Employee_Role()
    {
        var (client, companyId, employeeId) = await SetupAsync(EmployeeUserId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/toil",
            new
            {
                companyId,
                employeeId,
                awardedByEmployeeId = EmployeeUserId,
                days = 1.0,
                occurredOn = "2026-06-10"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_AwardToil_Returns_Created_For_HrAdministrator()
    {
        var (client, companyId, employeeId) = await SetupAsync(HrAdminUserId);
        await SeedToilLeaveTypeAsync(companyId);
        await SeedPolicyAssignmentAsync(companyId, employeeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/toil",
            new
            {
                companyId,
                employeeId,
                awardedByEmployeeId = HrAdminUserId,
                days = 0.5,
                occurredOn = "2026-06-10",
                notes = "Worked late"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AwardToilPayload>();
        Assert.NotNull(payload);
        Assert.Equal(0.5m, payload!.Days);
        Assert.Equal(0.5m, payload.BalanceRemainingDays);
    }

    [Fact]
    public async Task Post_AwardToil_Returns_Created_For_Manager()
    {
        var (client, companyId, employeeId) = await SetupAsync(ManagerUserId);
        await SeedToilLeaveTypeAsync(companyId);
        await SeedPolicyAssignmentAsync(companyId, employeeId);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/toil",
            new
            {
                companyId,
                employeeId,
                awardedByEmployeeId = ManagerUserId,
                days = 1.0,
                occurredOn = "2026-06-11"
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<(HttpClient Client, Guid CompanyId, Guid EmployeeId)> SetupAsync(Guid userId)
    {
        // Use HrAdmin for setup so employee:manage policy is satisfied regardless of userId
        var setupClient = _factory.CreateClient();
        setupClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUserId.ToString());
        setupClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, HrAdminUserId.ToString());

        var companyResp = await setupClient.PostAsJsonAsync("/api/companies", new
        {
            name = $"TOIL Test {Guid.NewGuid():N}",
            addresses = new[] { new { type = "RegisteredOffice", line1 = "1 Test St", city = "London", countryCode = "GB" } }
        });
        companyResp.EnsureSuccessStatusCode();
        var company = await companyResp.Content.ReadFromJsonAsync<CompanyPayload>();
        var companyId = company!.Id;

        setupClient.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        setupClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var empResp = await setupClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new { companyId, firstName = "TOIL", lastName = "Tester", workEmail = $"toil.{Guid.NewGuid():N}@example.com", startDate = "2026-01-01", dateOfBirth = "1990-01-01", nationality = "British", gender = "Male" });
        empResp.EnsureSuccessStatusCode();
        var employee = await empResp.Content.ReadFromJsonAsync<EmployeePayload>();

        // Return a client authenticated as the target userId, scoped to the created company
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        return (client, companyId, employee!.Id);
    }

    private async Task SeedToilLeaveTypeAsync(Guid companyId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var exists = db.LeaveTypes.Any(lt => lt.CompanyId == companyId && lt.Behaviour == LeaveTypeBehaviour.Toil);
        if (!exists)
        {
            db.LeaveTypes.Add(LeaveType.Create(
                Guid.NewGuid(), companyId, "Time Off In Lieu", "TOIL", 0,
                AccrualMethod.None, LeaveTypeBehaviour.Toil, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
    }

    private async Task SeedPolicyAssignmentAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var policyId = Guid.NewGuid();
        db.LeavePolicies.Add(LeavePolicy.Create(policyId, companyId, "TOIL Policy", null, 0, false, DateTimeOffset.UtcNow));
        db.EmployeeLeavePolicyAssignments.Add(EmployeeLeavePolicyAssignment.Create(
            Guid.NewGuid(), companyId, employeeId, policyId, new DateOnly(2026, 1, 1), DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
    }

    private sealed record CompanyPayload(Guid Id);
    private sealed record EmployeePayload(Guid Id);
    private sealed record AwardToilPayload(Guid TransactionId, Guid EmployeeId, decimal Days, decimal BalanceRemainingDays, string? Notes);
}
