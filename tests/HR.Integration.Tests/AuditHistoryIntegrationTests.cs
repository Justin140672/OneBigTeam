using System.Net.Http.Json;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// Closes a real coverage gap: every write handler that publishes an audit event is covered by a
/// unit test asserting the publish CALL happened against a fake/capturing IAuditEventPublisher,
/// but nothing previously verified the write actually landed in the audit table and comes back
/// out through GetEmployeeAuditHistory. These tests exercise the real HTTP endpoint, the real
/// DbAuditEventPublisher, and the real AuditDbContext/AuditHistoryReader end-to-end.
///
/// UpdateCompanySettings is the exception: its audit event is company-scoped (EmployeeId is never
/// set — see CompanySettingsUpdatedAuditEvent), so it can never appear in GetEmployeeAuditHistory
/// (which filters by EmployeeId) and there is no company-level audit-history endpoint. That test
/// instead reads AuditDbContext directly via the test host's DI container.
/// </summary>
[Collection("Integration")]
public class AuditHistoryIntegrationTests
{
    private readonly ApiWebApplicationFactory _factory;

    private static readonly Guid HrAdminUser = new("a0d10001-0000-0000-0000-000000000001");
    private static readonly Guid CompanyAdminUser = new("a0d10001-0000-0000-0000-000000000002");

    public AuditHistoryIntegrationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;

        Task.Run(async () =>
        {
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.HrAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, HrAdminUser, SystemRoles.Employee);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.CompanyAdministrator);
            await TestRoleSeeder.AssignRoleAsync(factory, CompanyAdminUser, SystemRoles.Employee);
        }).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task AdjustLeaveBalance_Creates_Audit_Record_Retrievable_Via_AuditHistory_Endpoint()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = AuthenticatedClient(companyId);

        var leaveTypeId = await CreateLeaveTypeAsync(companyId);

        var policyResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy {Guid.NewGuid():N}", carryOverDays = 0, allowNegativeBalance = false });
        policyResp.EnsureSuccessStatusCode();
        var policy = await policyResp.Content.ReadFromJsonAsync<IdPayload>();

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);

        var assignResp = await hrAdminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-policy",
            new { companyId, employeeId, leavePolicyId = policy!.Id, effectiveFrom = "2026-01-01" });
        assignResp.EnsureSuccessStatusCode();

        var adjustResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/leave-balance-adjustments",
            new
            {
                companyId,
                employeeId,
                leaveTypeId,
                adjustmentValue = 15m,
                reason = "Correction",
                comments = "Audit coverage test",
                allowNegativeOverride = false
            });
        adjustResp.EnsureSuccessStatusCode();

        var historyResp = await hrAdminClient.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/audit-history");
        historyResp.EnsureSuccessStatusCode();

        var history = await historyResp.Content.ReadFromJsonAsync<AuditHistoryPayload>();
        Assert.NotNull(history);

        var entry = Assert.Single(history!.Items, i => i.Module == "Leave" && i.Action.Contains("adjusted", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(entry.Changes, c => c.Field == "Adjustment Days" || c.Field == "Adjustment Hours");
    }

    [Fact]
    public async Task CreateCompensationRecord_Creates_Audit_Record_Retrievable_Via_AuditHistory_Endpoint()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);

        var compResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/compensation",
            new
            {
                companyId,
                employeeId,
                effectiveFrom = "2026-01-01",
                salaryType = "Annual",
                salary = 55000m,
                currency = "GBP",
                hoursPerWeek = 37.5m,
                fte = 1.0m,
                notes = "Audit coverage test"
            });
        compResp.EnsureSuccessStatusCode();

        var historyResp = await hrAdminClient.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/audit-history");
        historyResp.EnsureSuccessStatusCode();

        var history = await historyResp.Content.ReadFromJsonAsync<AuditHistoryPayload>();
        Assert.NotNull(history);

        var entry = Assert.Single(history!.Items, i => i.Action == "Compensation record created");
        Assert.Equal("Employees", entry.Module);
        Assert.Contains(entry.Changes, c => c.Field == "Salary" && c.After == "55000");
        Assert.Contains(entry.Changes, c => c.Field == "Currency" && c.After == "GBP");
    }

    [Fact]
    public async Task UpdateMyContactDetails_Creates_Audit_Record_Retrievable_Via_AuditHistory_Endpoint()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);

        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);

        using var employeeClient = _factory.CreateClient();
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var updateResp = await employeeClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/contact-details",
            new
            {
                personalEmail = "personal@example.com",
                addressLine1 = "1 Audit Test Street",
                city = "London",
                postCode = "SW1A 1AA",
                country = "United Kingdom"
            });
        updateResp.EnsureSuccessStatusCode();

        // employee:manage is required to read audit-history, so the self-service employee client
        // (a plain employee with no elevated role) can't fetch it — use the HR admin client instead.
        var historyResp = await hrAdminClient.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/audit-history");
        historyResp.EnsureSuccessStatusCode();

        var history = await historyResp.Content.ReadFromJsonAsync<AuditHistoryPayload>();
        Assert.NotNull(history);

        var entry = Assert.Single(history!.Items, i => i.Action == "Employee contact details updated");
        Assert.Equal("Employees", entry.Module);
        Assert.Contains(entry.Changes, c => c.Field == "Address Line1" && c.After == "1 Audit Test Street");
    }

    [Fact]
    public async Task UpdateEmployeeProfile_Creates_Audit_Record_Retrievable_Via_AuditHistory_Endpoint()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);

        var updateResp = await hrAdminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile",
            new
            {
                companyId,
                id = employeeId,
                firstName = "Audrey",
                lastName = "Tester",
                workEmail = $"audit.tester.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                gender = "Female",
                hasSystemAccess = true
            });
        updateResp.EnsureSuccessStatusCode();

        var historyResp = await hrAdminClient.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/audit-history");
        historyResp.EnsureSuccessStatusCode();

        var history = await historyResp.Content.ReadFromJsonAsync<AuditHistoryPayload>();
        Assert.NotNull(history);

        var entry = Assert.Single(history!.Items, i => i.Action == "Employee profile updated");
        Assert.Equal("Employees", entry.Module);
        Assert.Contains(entry.Changes, c => c.Field == "First Name" && c.After == "Audrey");
        Assert.Contains(entry.Changes, c => c.Field == "Gender" && c.After == "Female");

        // GetEmployeeAuditHistoryHandler.ResolveUser renders "System" only when ActorEmployeeId is
        // null. The HR admin has no seeded Employee record in this test, so the name itself can't
        // resolve ("Unknown" is expected) — but proving it isn't "System" confirms the acting
        // employee id (from the "sub" claim) is actually reaching the audit event, not being lost.
        Assert.NotEqual("System", entry.User);
    }

    [Fact]
    public async Task UpdateEmployeeProfile_Changing_Department_Resolves_Department_Name_In_AuditHistory()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);

        var newDeptResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"NewDept-{Guid.NewGuid():N}" });
        newDeptResp.EnsureSuccessStatusCode();
        var newDepartment = await newDeptResp.Content.ReadFromJsonAsync<DepartmentPayload>();

        var updateResp = await hrAdminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile",
            new
            {
                companyId,
                id = employeeId,
                departmentId = newDepartment!.Id,
                firstName = "Audrey",
                lastName = "Tester",
                workEmail = $"audit.tester.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                gender = "Female",
                hasSystemAccess = true
            });
        updateResp.EnsureSuccessStatusCode();

        var historyResp = await hrAdminClient.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/audit-history");
        historyResp.EnsureSuccessStatusCode();

        var history = await historyResp.Content.ReadFromJsonAsync<AuditHistoryPayload>();
        Assert.NotNull(history);

        var entry = Assert.Single(history!.Items, i => i.Action == "Employee profile updated");
        Assert.Equal("Employees", entry.Module);

        var departmentChange = Assert.Single(entry.Changes, c => c.Field == "Department");
        Assert.Equal(newDepartment.Name, departmentChange.After);
    }

    [Fact]
    public async Task UpdateEmployeeProfile_And_UpdateEmploymentDetails_With_Same_CorrelationId_Merge_Into_One_AuditHistory_Entry()
    {
        // Ticket: EmployeeEdit.razor's combined Save action generates one CorrelationId and passes
        // it into both UpdateEmployeeProfile and UpdateEmploymentDetails so their two audit rows
        // read back as a single merged entry rather than two separate ones.
        var companyId = Guid.NewGuid();
        using var hrAdminClient = AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);
        var correlationId = Guid.NewGuid();

        var profileResp = await hrAdminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/profile",
            new
            {
                companyId,
                id = employeeId,
                firstName = "Audrey",
                lastName = "Tester",
                workEmail = $"audit.tester.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                gender = "Female",
                hasSystemAccess = true,
                correlationId
            });
        profileResp.EnsureSuccessStatusCode();

        var employmentResp = await hrAdminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/employment",
            new
            {
                companyId,
                id = employeeId,
                status = "Active",
                startDate = "2026-01-01",
                employeeNumber = "EMP-CORR-9999",
                correlationId
            });
        employmentResp.EnsureSuccessStatusCode();

        var historyResp = await hrAdminClient.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/audit-history");
        historyResp.EnsureSuccessStatusCode();

        var history = await historyResp.Content.ReadFromJsonAsync<AuditHistoryPayload>();
        Assert.NotNull(history);

        var entry = Assert.Single(history!.Items, i => i.Action == "Employee profile and employment details updated");
        Assert.Contains(entry.Changes, c => c.Field == "First Name" && c.After == "Audrey");
        Assert.Contains(entry.Changes, c => c.Field == "Employee Number" && c.After == "EMP-CORR-9999");
    }

    [Fact]
    public async Task UpdateCompanySettings_Persists_Audit_Record()
    {
        // No API surface exposes company-level audit events (GetEmployeeAuditHistory is scoped to
        // a single employee, and CompanySettingsUpdatedAuditEvent never sets EmployeeId) — so this
        // reads the audit table directly via the test host's own DI container instead.
        //
        // Must be CompanyAdministrator, not HrAdministrator — the company:manage policy that
        // guards UpdateCompanySettings is CompanyAdministrator-only.
        using var companyAdminClient = _factory.CreateClient();
        companyAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, CompanyAdminUser.ToString());
        // Placeholder tenant header for the creation call itself (RequireTenantMiddleware
        // requires one on every authenticated request) — swapped for the real company id below.
        companyAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, CompanyAdminUser.ToString());

        var createResp = await companyAdminClient.PostAsJsonAsync("/api/companies", new
        {
            name = $"Audit Test {Guid.NewGuid():N}",
            addresses = new[]
            {
                new { type = "RegisteredOffice", line1 = "10 High Street", city = "London", countryCode = "GB" }
            }
        });
        createResp.EnsureSuccessStatusCode();
        var company = await createResp.Content.ReadFromJsonAsync<IdPayload>();

        companyAdminClient.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        companyAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, company!.Id.ToString());

        var settingsResp = await companyAdminClient.PutAsJsonAsync($"/api/companies/{company.Id}/settings", new
        {
            timeZone = "Europe/London",
            locale = "en-GB",
            workingDays = 31,
            hoursPerDay = 7.5,
            leaveYearStartMonth = 4,
            defaultHolidayAllowance = 28,
            probationMonths = 3
        });
        settingsResp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == company.Id && e.EventType == "company-settings.updated")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("CompanySettings", auditRecord!.EntityType);
        Assert.Equal(company.Id, auditRecord.EntityId);
        Assert.Contains("Europe/London", auditRecord.AfterJson);
    }

    [Fact]
    public async Task CreatePositionProfile_Persists_Audit_Record()
    {
        // PositionProfileCreatedAuditEvent is scoped by ActorEmployeeId, not EmployeeId (it records
        // who managed the position profile, not an employee the profile belongs to), so like
        // CompanySettings it can never appear via GetEmployeeAuditHistory. Read the audit table
        // directly via the test host's own DI container instead.
        var companyId = Guid.NewGuid();
        using var hrAdminClient = AuthenticatedClient(companyId);

        var (departmentId, locationId, leavePolicyId) = await CreatePositionProfileReferenceDataAsync(hrAdminClient, companyId);

        var createResp = await hrAdminClient.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = $"Audit Role {Guid.NewGuid():N}"
        });
        createResp.EnsureSuccessStatusCode();
        var positionProfile = await createResp.Content.ReadFromJsonAsync<IdPayload>();

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "position-profile.created")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("PositionProfile", auditRecord!.EntityType);
        Assert.Equal(positionProfile!.Id, auditRecord.EntityId);
        Assert.Equal(HrAdminUser, auditRecord.ActorEmployeeId);
        Assert.Null(auditRecord.BeforeJson);
        Assert.NotNull(auditRecord.AfterJson);
    }

    [Fact]
    public async Task UpdatePositionProfile_Persists_Audit_Record_With_Before_And_After()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = AuthenticatedClient(companyId);

        var (departmentId, locationId, leavePolicyId) = await CreatePositionProfileReferenceDataAsync(hrAdminClient, companyId);

        var createResp = await hrAdminClient.PostAsJsonAsync($"/api/companies/{companyId}/position-profiles", new
        {
            companyId,
            departmentId,
            locationId,
            defaultLeavePolicyId = leavePolicyId,
            title = $"Audit Role {Guid.NewGuid():N}"
        });
        createResp.EnsureSuccessStatusCode();
        var positionProfile = await createResp.Content.ReadFromJsonAsync<IdPayload>();

        var newTitle = $"Updated Audit Role {Guid.NewGuid():N}";
        var updateResp = await hrAdminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles/{positionProfile!.Id}",
            new
            {
                companyId,
                id = positionProfile.Id,
                departmentId,
                locationId,
                defaultLeavePolicyId = leavePolicyId,
                title = newTitle
            });
        updateResp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "position-profile.updated")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("PositionProfile", auditRecord!.EntityType);
        Assert.Equal(positionProfile.Id, auditRecord.EntityId);
        Assert.Equal(HrAdminUser, auditRecord.ActorEmployeeId);
        Assert.NotNull(auditRecord.BeforeJson);
        Assert.NotNull(auditRecord.AfterJson);
        Assert.Contains(newTitle, auditRecord.AfterJson);
    }

    private static async Task<(Guid DepartmentId, Guid LocationId, Guid LeavePolicyId)> CreatePositionProfileReferenceDataAsync(HttpClient client, Guid companyId)
    {
        var deptResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept-{Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType-{Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc-{Guid.NewGuid():N}", locationTypeId });
        locResp.EnsureSuccessStatusCode();
        var locationId = (await locResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy-{Guid.NewGuid():N}", carryOverDays = 5, allowNegativeBalance = false });
        leavePolicyResp.EnsureSuccessStatusCode();
        var leavePolicyId = (await leavePolicyResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (departmentId, locationId, leavePolicyId);
    }

    private HttpClient AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    private async Task<Guid> CreateLeaveTypeAsync(Guid companyId, int defaultEntitlementDays = 25)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();
        var leaveTypeId = Guid.NewGuid();
        db.LeaveTypes.Add(LeaveType.Create(
            leaveTypeId, companyId, "Annual Leave", "ANNUAL", defaultEntitlementDays,
            AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        return leaveTypeId;
    }

    private static async Task<Guid> CreateEmployeeAsync(HttpClient client, Guid companyId)
    {
        var (departmentId, locationId, positionProfileId, employmentTypeId) =
            await CreateEmployeeReferenceDataAsync(client, companyId);

        var resp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees",
            new
            {
                companyId,
                firstName = "Audit",
                lastName = "Tester",
                workEmail = $"audit.tester.{Guid.NewGuid():N}@example.com",
                startDate = "2026-01-01",
                dateOfBirth = "1990-01-01",
                nationality = "British",
                gender = "Male",
                employeeNumber = $"AUD-{Guid.NewGuid():N}",
                employmentTypeId,
                departmentId,
                locationId,
                positionProfileId
            });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<IdPayload>();
        return payload!.Id;
    }

    private static async Task<(Guid DepartmentId, Guid LocationId, Guid PositionProfileId, Guid EmploymentTypeId)>
        CreateEmployeeReferenceDataAsync(HttpClient client, Guid companyId)
    {
        var deptResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/departments",
            new { companyId, name = $"Dept-{Guid.NewGuid():N}" });
        deptResp.EnsureSuccessStatusCode();
        var departmentId = (await deptResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locTypeResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/location-types",
            new { companyId, name = $"LocType-{Guid.NewGuid():N}" });
        locTypeResp.EnsureSuccessStatusCode();
        var locationTypeId = (await locTypeResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var locResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/locations",
            new { companyId, name = $"Loc-{Guid.NewGuid():N}", locationTypeId });
        locResp.EnsureSuccessStatusCode();
        var locationId = (await locResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var leavePolicyResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/leave-policies",
            new { companyId, name = $"Policy-{Guid.NewGuid():N}", carryOverDays = 5, allowNegativeBalance = false });
        leavePolicyResp.EnsureSuccessStatusCode();
        var defaultLeavePolicyId = (await leavePolicyResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var ppResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/position-profiles",
            new { companyId, departmentId, locationId, title = $"Role-{Guid.NewGuid():N}", defaultLeavePolicyId });
        ppResp.EnsureSuccessStatusCode();
        var positionProfileId = (await ppResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var etResp = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/employment-types",
            new { companyId, name = $"EmpType-{Guid.NewGuid():N}" });
        etResp.EnsureSuccessStatusCode();
        var employmentTypeId = (await etResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        return (departmentId, locationId, positionProfileId, employmentTypeId);
    }

    private sealed record IdPayload(Guid Id);

    private sealed record DepartmentPayload(Guid Id, string Name);

    private sealed record AuditFieldChangePayload(string Field, string Before, string After);

    private sealed record AuditHistoryItemPayload(
        DateTimeOffset OccurredAt, string Action, string Module, string User, List<AuditFieldChangePayload> Changes);

    private sealed record AuditHistoryPayload(List<AuditHistoryItemPayload> Items);
}
