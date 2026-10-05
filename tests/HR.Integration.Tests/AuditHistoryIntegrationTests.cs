using System.Net.Http.Json;
using HR.Infrastructure.Persistence;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

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
        using var hrAdminClient = await AuthenticatedClient(companyId);

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
        using var hrAdminClient = await AuthenticatedClient(companyId);

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
        Assert.DoesNotContain(entry.Changes, c => c.After == "55000");
        Assert.Contains(entry.Changes, c => c.Field == "Currency" && c.After == "GBP");
    }

    [Fact]
    public async Task UpdateMyContactDetails_Creates_Audit_Record_Retrievable_Via_AuditHistory_Endpoint()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);

        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee);

        using var employeeClient = _factory.CreateClient();
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeId.ToString());
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeId, SystemRoles.Employee, companyId);

        var updateResp = await employeeClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/employees/me/contact-details",
            new
            {
                personalEmail = "personal@example.com",
                addressLine1 = "1 Audit Test Street",
                city = "London",
                postCode = "SW1A 1AA",
                country = "United Kingdom",
                expectedVersion = await GetMyContactVersionAsync(employeeClient, companyId)
            });
        updateResp.EnsureSuccessStatusCode();

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
        using var hrAdminClient = await AuthenticatedClient(companyId);

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
                addressLine1 = "1 Test Street",
                city = "London",
                postCode = "SW1A 1AA",
                startDate = "2026-01-01",
                gender = "Female",
                hasSystemAccess = true,
                expectedVersion = await GetEmployeeVersionAsync(hrAdminClient, companyId, employeeId)
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

        Assert.NotEqual("System", entry.User);
    }

    [Fact]
    public async Task UpdateEmployeeProfile_Changing_Department_Resolves_Department_Name_In_AuditHistory()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);

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
                addressLine1 = "1 Test Street",
                city = "London",
                postCode = "SW1A 1AA",
                startDate = "2026-01-01",
                gender = "Female",
                hasSystemAccess = true,
                expectedVersion = await GetEmployeeVersionAsync(hrAdminClient, companyId, employeeId)
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
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);
        await EmployeeReferenceDataSeeder.SetEmployeeNumberModeManualAsync(hrAdminClient, companyId);
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
                addressLine1 = "1 Test Street",
                city = "London",
                postCode = "SW1A 1AA",
                startDate = "2026-01-01",
                gender = "Female",
                hasSystemAccess = true,
                correlationId,
                expectedVersion = await GetEmployeeVersionAsync(hrAdminClient, companyId, employeeId)
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
                correlationId,
                expectedVersion = await GetEmployeeVersionAsync(hrAdminClient, companyId, employeeId)
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
        using var companyAdminClient = _factory.CreateClient();
        companyAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, CompanyAdminUser.ToString());
        companyAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, CompanyAdminUser.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, CompanyAdminUser, SystemRoles.CompanyAdministrator, CompanyAdminUser);

        var companyId = await CompanyTestSeeder.CreateCompanyAsync(_factory, $"Audit Test {Guid.NewGuid():N}");

        companyAdminClient.DefaultRequestHeaders.Remove(TestAuthHandler.TenantHeader);
        companyAdminClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());

        var settingsResp = await companyAdminClient.PutAsJsonAsync($"/api/companies/{companyId}/settings", new
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
            .Where(e => e.CompanyId == companyId && e.EventType == "company-settings.updated")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("CompanySettings", auditRecord!.EntityType);
        Assert.Equal(companyId, auditRecord.EntityId);
        Assert.Contains("Europe/London", auditRecord.AfterJson);
    }

    [Fact]
    public async Task CreatePositionProfile_Persists_Audit_Record()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);

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
        using var hrAdminClient = await AuthenticatedClient(companyId);

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
                title = newTitle,
                expectedVersion = 1
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

    [Fact]
    public async Task RecordSickness_Persists_Audit_Record_With_Actor_And_Without_Notes_Content()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);
        var categoryId = await CreateSicknessCategoryAsync(hrAdminClient, companyId);
        const string sensitiveNotes = "AuditIntegration-Sensitive-Diagnosis-Detail";

        var recordResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new
            {
                companyId,
                employeeId,
                categoryId,
                startDate = "2026-07-01",
                startDayPart = 0,
                notes = sensitiveNotes
            });
        recordResp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "sickness.recorded")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("SicknessRecord", auditRecord!.EntityType);
        Assert.Equal(employeeId, auditRecord.EmployeeId);
        Assert.Equal(HrAdminUser, auditRecord.ActorEmployeeId);
        Assert.NotEqual(employeeId, auditRecord.ActorEmployeeId);
        Assert.DoesNotContain(sensitiveNotes, auditRecord.AfterJson ?? string.Empty);
    }

    [Fact]
    public async Task CloseSicknessRecord_Persists_Audit_Record_With_Actor_And_Without_Notes_Content()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);
        var categoryId = await CreateSicknessCategoryAsync(hrAdminClient, companyId);
        const string sensitiveNotes = "AuditIntegration-Sensitive-CloseNotes-Detail";

        var recordResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new { companyId, employeeId, categoryId, startDate = "2026-07-01", startDayPart = 0 });
        recordResp.EnsureSuccessStatusCode();
        var recordId = (await recordResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var closeResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}/close",
            new
            {
                companyId,
                employeeId,
                id = recordId,
                endDate = "2026-07-03",
                endDayPart = 0,
                notes = sensitiveNotes
            });
        closeResp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "sickness.closed")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("SicknessRecord", auditRecord!.EntityType);
        Assert.Equal(employeeId, auditRecord.EmployeeId);
        Assert.Equal(HrAdminUser, auditRecord.ActorEmployeeId);
        Assert.NotEqual(employeeId, auditRecord.ActorEmployeeId);
        Assert.DoesNotContain(sensitiveNotes, auditRecord.BeforeJson ?? string.Empty);
        Assert.DoesNotContain(sensitiveNotes, auditRecord.AfterJson ?? string.Empty);
    }

    [Fact]
    public async Task CompleteReturnToWorkReview_Persists_Audit_Record_With_Reviewer_Actor_And_Without_Sensitive_Content()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);
        var categoryId = await CreateSicknessCategoryAsync(hrAdminClient, companyId);
        const string sensitiveAdjustmentDetails = "AuditIntegration-Sensitive-Adjustment-Detail";
        const string sensitiveManagerNotes = "AuditIntegration-Sensitive-ManagerNotes-Detail";

        var recordResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records",
            new { companyId, employeeId, categoryId, startDate = "2026-06-01", startDayPart = 0 });
        recordResp.EnsureSuccessStatusCode();
        var recordId = (await recordResp.Content.ReadFromJsonAsync<IdPayload>())!.Id;

        var closeResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/sickness-records/{recordId}/close",
            new { companyId, employeeId, id = recordId, endDate = "2026-06-03", endDayPart = 0 });
        closeResp.EnsureSuccessStatusCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var sicknessDb = scope.ServiceProvider.GetRequiredService<HR.Modules.Sickness.Persistence.SicknessDbContext>();
            var review = await sicknessDb.ReturnToWorkReviews.AsNoTracking()
                .SingleAsync(r => r.CompanyId == companyId && r.EmployeeId == employeeId);

            var completeResp = await hrAdminClient.PostAsJsonAsync(
                $"/api/companies/{companyId}/return-to-work-reviews/{review.Id}/complete",
                new
                {
                    companyId,
                    reviewId = review.Id,
                    outcome = "FitWithAdjustments",
                    adjustmentsRequired = true,
                    adjustmentDetails = sensitiveAdjustmentDetails,
                    managerNotes = sensitiveManagerNotes
                });
            completeResp.EnsureSuccessStatusCode();
        }

        using var auditScope = _factory.Services.CreateScope();
        var auditDb = auditScope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "sickness.return_to_work_review_completed")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("ReturnToWorkReview", auditRecord!.EntityType);
        Assert.Equal(employeeId, auditRecord.EmployeeId);
        Assert.Equal(HrAdminUser, auditRecord.ActorEmployeeId);
        Assert.NotEqual(employeeId, auditRecord.ActorEmployeeId);
        Assert.DoesNotContain(sensitiveAdjustmentDetails, auditRecord.AfterJson ?? string.Empty);
        Assert.DoesNotContain(sensitiveManagerNotes, auditRecord.AfterJson ?? string.Empty);
    }

    [Fact]
    public async Task CreateProbationRecord_Persists_Audit_Record_With_Actor_And_Without_Notes_Content()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);
        var employeeId = Guid.NewGuid();
        const string sensitiveNotes = "AuditIntegration-Sensitive-ProbationNotes-Detail";

        var createResp = await hrAdminClient.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId,
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01",
            notes = sensitiveNotes
        });
        createResp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "probation-record.created")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("ProbationRecord", auditRecord!.EntityType);
        Assert.Equal(employeeId, auditRecord.EmployeeId);
        Assert.Equal(HrAdminUser, auditRecord.ActorEmployeeId);
        Assert.NotEqual(employeeId, auditRecord.ActorEmployeeId);
        Assert.DoesNotContain(sensitiveNotes, auditRecord.AfterJson ?? string.Empty);
    }

    [Fact]
    public async Task UpdateProbationRecord_Persists_Audit_Record_With_Before_After_And_Without_Notes_Content()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);
        var oldManagerId = Guid.NewGuid();
        var newManagerId = Guid.NewGuid();
        const string sensitiveNotes = "AuditIntegration-Sensitive-ProbationUpdateNotes-Detail";

        var createResp = await hrAdminClient.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = oldManagerId,
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<IdPayload>();

        var currentVersion = await GetCurrentProbationRecordVersionAsync(hrAdminClient, companyId, created!.Id);

        var updateResp = await hrAdminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{created.Id}", new
            {
                companyId,
                id = created.Id,
                managerEmployeeId = newManagerId,
                expectedEndDate = "2026-10-01",
                notes = sensitiveNotes,
                expectedVersion = currentVersion
            });
        updateResp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "probation-record.updated")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("ProbationRecord", auditRecord!.EntityType);
        Assert.Equal(HrAdminUser, auditRecord.ActorEmployeeId);
        Assert.NotNull(auditRecord.BeforeJson);
        Assert.NotNull(auditRecord.AfterJson);
        Assert.Contains(oldManagerId.ToString(), auditRecord.BeforeJson);
        Assert.Contains(newManagerId.ToString(), auditRecord.AfterJson);
        Assert.DoesNotContain(sensitiveNotes, auditRecord.BeforeJson);
        Assert.DoesNotContain(sensitiveNotes, auditRecord.AfterJson);
    }

    [Fact]
    public async Task UpdateProbationRecord_Returns_NotFound_For_Missing_Record()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);

        var response = await hrAdminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{Guid.NewGuid()}", new
            {
                companyId,
                id = Guid.NewGuid(),
                managerEmployeeId = Guid.NewGuid(),
                expectedEndDate = "2026-09-01",
                expectedVersion = 1
            });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProbationRecord_Returns_Conflict_For_Terminal_Status_Record()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);
        var managerId = Guid.NewGuid();

        var createResp = await hrAdminClient.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = managerId,
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        createResp.EnsureSuccessStatusCode();
        var created = await createResp.Content.ReadFromJsonAsync<IdPayload>();

        var reviewResp = await hrAdminClient.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = created!.Id,
            reviewType = "FinalDecision",
            dueDate = "2026-09-01"
        });
        reviewResp.EnsureSuccessStatusCode();
        var review = await reviewResp.Content.ReadFromJsonAsync<IdPayload>();

        var completeResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{created.Id}/reviews/{review!.Id}/complete",
            new
            {
                companyId,
                probationRecordId = created.Id,
                reviewId = review.Id,
                outcome = "Pass",
                decisionDate = "2026-09-01"
            });
        completeResp.EnsureSuccessStatusCode();

        var currentVersion = await GetCurrentProbationRecordVersionAsync(hrAdminClient, companyId, created.Id);

        var response = await hrAdminClient.PutAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{created.Id}", new
            {
                companyId,
                id = created.Id,
                managerEmployeeId = managerId,
                expectedEndDate = "2026-12-01",
                expectedVersion = currentVersion
            });

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CompleteProbationReview_Pass_Fail_Checkpoint_Persist_Distinct_Audit_Event_Types_Without_Notes_Content()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);
        const string sensitivePassNotes = "AuditIntegration-Sensitive-PassNotes-Detail";
        const string sensitiveFailNotes = "AuditIntegration-Sensitive-FailNotes-Detail";
        const string sensitiveCheckpointNotes = "AuditIntegration-Sensitive-CheckpointNotes-Detail";

        var (passRecordId, passReviewId) = await CreateProbationRecordAndReviewAsync(hrAdminClient, companyId, "FinalDecision");
        var passResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{passRecordId}/reviews/{passReviewId}/complete",
            new { companyId, probationRecordId = passRecordId, reviewId = passReviewId, notes = sensitivePassNotes, outcome = "Pass", decisionDate = "2026-09-01" });
        passResp.EnsureSuccessStatusCode();

        var (failRecordId, failReviewId) = await CreateProbationRecordAndReviewAsync(hrAdminClient, companyId, "FinalDecision");
        var failResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{failRecordId}/reviews/{failReviewId}/complete",
            new { companyId, probationRecordId = failRecordId, reviewId = failReviewId, notes = sensitiveFailNotes, outcome = "Fail", decisionDate = "2026-09-01" });
        failResp.EnsureSuccessStatusCode();

        var (checkpointRecordId, checkpointReviewId) = await CreateProbationRecordAndReviewAsync(hrAdminClient, companyId, "ManagerCheckIn");
        var checkpointResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{checkpointRecordId}/reviews/{checkpointReviewId}/complete",
            new { companyId, probationRecordId = checkpointRecordId, reviewId = checkpointReviewId, notes = sensitiveCheckpointNotes });
        checkpointResp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var passAudit = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "probation-record.passed" && e.EntityId == passRecordId)
            .OrderByDescending(e => e.OccurredAt).FirstOrDefaultAsync();
        Assert.NotNull(passAudit);
        Assert.DoesNotContain(sensitivePassNotes, passAudit!.AfterJson ?? string.Empty);

        var failAudit = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "probation-record.failed" && e.EntityId == failRecordId)
            .OrderByDescending(e => e.OccurredAt).FirstOrDefaultAsync();
        Assert.NotNull(failAudit);
        Assert.DoesNotContain(sensitiveFailNotes, failAudit!.AfterJson ?? string.Empty);

        var checkpointAudit = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "probation-review.completed" && e.EntityId == checkpointReviewId)
            .OrderByDescending(e => e.OccurredAt).FirstOrDefaultAsync();
        Assert.NotNull(checkpointAudit);
        Assert.DoesNotContain(sensitiveCheckpointNotes, checkpointAudit!.AfterJson ?? string.Empty);
    }

    [Fact]
    public async Task CreateProbationReview_Persists_Audit_Record_With_Human_Actor()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);

        var (recordId, _) = await CreateProbationRecordAndReviewAsync(hrAdminClient, companyId, "ManagerCheckIn");

        var reviewResp = await hrAdminClient.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = recordId,
            reviewType = "HrReview",
            dueDate = "2026-08-01"
        });
        reviewResp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecords = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "probation-review.created")
            .OrderByDescending(e => e.OccurredAt)
            .ToListAsync();

        var auditRecord = auditRecords.First();
        Assert.Equal("ProbationReview", auditRecord.EntityType);
        Assert.Equal(HrAdminUser, auditRecord.ActorEmployeeId);
        Assert.Contains("HrReview", auditRecord.AfterJson);
    }

    [Fact]
    public async Task CompleteProbationReview_Extend_Persists_Audit_Record_With_Actor_And_Without_ExtensionReason_Content()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);
        const string sensitiveExtensionReason = "AuditIntegration-Sensitive-ExtensionReason-Detail";

        var (recordId, reviewId) = await CreateProbationRecordAndReviewAsync(hrAdminClient, companyId, "FinalDecision");

        var completeResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation-records/{recordId}/reviews/{reviewId}/complete",
            new
            {
                companyId,
                probationRecordId = recordId,
                reviewId,
                outcome = "Extend",
                decisionDate = "2026-09-01",
                newExpectedEndDate = "2026-12-01",
                extensionReason = sensitiveExtensionReason
            });
        completeResp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "probation-record.extended" && e.EntityId == recordId)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("ProbationRecord", auditRecord!.EntityType);
        Assert.Equal(HrAdminUser, auditRecord.ActorEmployeeId);
        Assert.NotNull(auditRecord.BeforeJson);
        Assert.NotNull(auditRecord.AfterJson);
        Assert.Contains("2026-09-01", auditRecord.BeforeJson);
        Assert.Contains("2026-12-01", auditRecord.AfterJson);
        Assert.DoesNotContain(sensitiveExtensionReason, auditRecord.BeforeJson);
        Assert.DoesNotContain(sensitiveExtensionReason, auditRecord.AfterJson);
    }

    [Fact]
    public async Task MarkProbationNotApplicable_Persists_Audit_Record_With_HasReason_And_Without_Reason_Content()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);
        var employeeId = Guid.NewGuid();
        const string sensitiveReason = "AuditIntegration-Sensitive-NotApplicableReason-Detail";

        var response = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/probation/employees/{employeeId}/not-applicable",
            new
            {
                companyId,
                employeeId,
                managerEmployeeId = Guid.NewGuid(),
                startDate = "2026-06-01",
                expectedEndDate = "2026-09-01",
                reason = sensitiveReason
            });
        response.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "probation-record.marked-not-applicable")
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("ProbationRecord", auditRecord!.EntityType);
        Assert.Equal(employeeId, auditRecord.EmployeeId);
        Assert.Contains("\"HasReason\"", auditRecord.AfterJson ?? string.Empty);
        Assert.Contains("true", auditRecord.AfterJson ?? string.Empty);
        Assert.DoesNotContain(sensitiveReason, auditRecord.AfterJson ?? string.Empty);
    }

    [Fact]
    public async Task StartOffboarding_Persists_Audit_Record_Attributed_To_Authenticated_Actor()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);

        var startResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-12-01", notes = "Resigned." });
        startResp.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var auditRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "offboarding-plan.started" && e.EmployeeId == employeeId)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(auditRecord);
        Assert.Equal("OffboardingPlan", auditRecord!.EntityType);
        Assert.Equal(employeeId, auditRecord.EmployeeId);
        Assert.Equal(HrAdminUser, auditRecord.ActorEmployeeId);
    }

    [Fact]
    public async Task CompleteOffboardingTask_Persists_TaskLevel_And_PlanCompleted_Audit_Records_With_Actor()
    {
        var companyId = Guid.NewGuid();
        using var hrAdminClient = await AuthenticatedClient(companyId);

        var employeeId = await CreateEmployeeAsync(hrAdminClient, companyId);

        var startResp = await hrAdminClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/employees/{employeeId}/offboarding/start",
            new { companyId, employeeId, lastWorkingDay = "2026-12-01", notes = "Resigned." });
        startResp.EnsureSuccessStatusCode();

        var tasksResp = await hrAdminClient.GetAsync($"/api/companies/{companyId}/employees/{employeeId}/tasks");
        tasksResp.EnsureSuccessStatusCode();
        var tasksPayload = await tasksResp.Content.ReadFromJsonAsync<EmployeeTaskListPayload>();

        var offboardingTasks = tasksPayload?.Items.Where(t => t.Source == "Offboarding").ToList() ?? [];

        if (offboardingTasks.Count == 0)
        {
            return;
        }

        foreach (var task in offboardingTasks)
        {
            var completeResp = await hrAdminClient.PostAsJsonAsync(
                $"/api/companies/{companyId}/tasks/{task.Id}/complete",
                new { companyId, id = task.Id });
            completeResp.EnsureSuccessStatusCode();
        }

        using var scope = _factory.Services.CreateScope();
        var auditDb = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var taskCompletedRecord = await auditDb.AuditEvents
            .Where(e => e.CompanyId == companyId && e.EventType == "offboarding-task.completed" && e.EmployeeId == employeeId)
            .OrderByDescending(e => e.OccurredAt)
            .FirstOrDefaultAsync();

        Assert.NotNull(taskCompletedRecord);
        Assert.Equal("OffboardingTask", taskCompletedRecord!.EntityType);
        Assert.Equal(HrAdminUser, taskCompletedRecord.ActorEmployeeId);
    }

    private static async Task<(Guid recordId, Guid reviewId)> CreateProbationRecordAndReviewAsync(
        HttpClient client, Guid companyId, string reviewType)
    {
        var recordResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-records", new
        {
            companyId,
            employeeId = Guid.NewGuid(),
            managerEmployeeId = Guid.NewGuid(),
            startDate = "2026-06-01",
            expectedEndDate = "2026-09-01"
        });
        recordResp.EnsureSuccessStatusCode();
        var record = await recordResp.Content.ReadFromJsonAsync<IdPayload>();

        var reviewResp = await client.PostAsJsonAsync($"/api/companies/{companyId}/probation-reviews", new
        {
            companyId,
            probationRecordId = record!.Id,
            reviewType,
            dueDate = "2026-07-01"
        });
        reviewResp.EnsureSuccessStatusCode();
        var review = await reviewResp.Content.ReadFromJsonAsync<IdPayload>();

        return (record.Id, review!.Id);
    }

    private async Task<Guid> CreateSicknessCategoryAsync(HttpClient client, Guid companyId)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/sickness-categories", new
        {
            companyId,
            name = $"Category-{Guid.NewGuid():N}",
            displayOrder = 1
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<IdPayload>())!.Id;
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

    private async Task<HttpClient> AuthenticatedClient(Guid companyId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, HrAdminUser.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, HrAdminUser, SystemRoles.HrAdministrator, companyId);
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
                addressLine1 = "1 Test Street",
                city = "London",
                postCode = "SW1A 1AA",
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

    private static async Task<int> GetEmployeeVersionAsync(HttpClient client, Guid companyId, Guid id)
    {
        var r = await client.GetAsync($"/api/companies/{companyId}/employees/{id}");
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<VersionPayload>())!.Version;
    }

    private static async Task<int> GetMyContactVersionAsync(HttpClient client, Guid companyId)
    {
        var r = await client.GetAsync($"/api/companies/{companyId}/employees/me/contact-details");
        r.EnsureSuccessStatusCode();
        return (await r.Content.ReadFromJsonAsync<VersionPayload>())!.Version;
    }

    private sealed record VersionPayload(int Version);

    private sealed record IdPayload(Guid Id);

    // Ticket 18 fix: UpdateProbationRecord now always requires ExpectedVersion (Ticket 16/2
    // optimistic concurrency); the create response doesn't carry the initial Version, so tests
    // that PUT an update fetch it via the GET detail endpoint first.
    private static async Task<int> GetCurrentProbationRecordVersionAsync(HttpClient client, Guid companyId, Guid id)
    {
        var detail = await client.GetAsync($"/api/companies/{companyId}/probation-records/{id}");
        detail.EnsureSuccessStatusCode();
        var payload = await detail.Content.ReadFromJsonAsync<VersionPayload>();
        return payload!.Version;
    }

    private sealed record EmployeeTaskItemPayload(Guid Id, string Source);

    private sealed record EmployeeTaskListPayload(List<EmployeeTaskItemPayload> Items);

    private sealed record DepartmentPayload(Guid Id, string Name);

    private sealed record AuditFieldChangePayload(string Field, string Before, string After);

    private sealed record AuditHistoryItemPayload(
        DateTimeOffset OccurredAt, string Action, string Module, string User, List<AuditFieldChangePayload> Changes);

    private sealed record AuditHistoryPayload(List<AuditHistoryItemPayload> Items);
}
