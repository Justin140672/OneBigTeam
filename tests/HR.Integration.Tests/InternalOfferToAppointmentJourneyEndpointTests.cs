using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Leave.Persistence;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Probation.Persistence;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Tasks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HR.Integration.Tests.Infrastructure.InternalAppointmentTestSeeder;
using static HR.Integration.Tests.Infrastructure.InternalOfferTestHelpers;

namespace HR.Integration.Tests;

/// <summary>
/// Internal vacancy offers: POST .../applications/{a}/appoint now requires the employee to have ACCEPTED the
/// offer and defaults to the accepted terms (start date, manager, compensation). A differing material term
/// is refused with "revised offer". Also covers the end-to-end journey offer -> view -> accept -> appoint and
/// the legacy offer path (accepted before the snapshot existed, appointed with an explicit manager and date).
/// </summary>
[Collection("Integration")]
public class InternalOfferToAppointmentJourneyEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public InternalOfferToAppointmentJourneyEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static readonly Action<Dictionary<string, object?>> StartsToday =
        b => b["proposedStartDate"] = Today.ToString("yyyy-MM-dd");

    [Fact]
    public async Task Full_Journey_Offer_View_Accept_Appoint_Closes_The_Task_And_Changes_The_Employee()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, recruiterId) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);
        using var employee = await EmployeeClientAsync(_factory, s);

        await MakeOfferOkAsync(recruiter, s, StartsToday);
        var viewed = await GetOfferOkAsync(employee, s);
        Assert.True(viewed.GetProperty("canRespond").GetBoolean());
        await RespondOkAsync(employee, s, "Accept", viewed.GetProperty("terms").GetProperty("offerVersion").GetInt32());
        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), new { });

        await AssertStatusAsync(HttpStatusCode.OK, response);
        var appointed = await ReadAppointResponseAsync(response);
        Assert.True(appointed.IsApplied);
        Assert.Equal("Completed", appointed.AppointmentStatus);
        Assert.Equal(s.HiredStageId, appointed.CurrentStageId);

        var task = Assert.Single(await GetOfferTasksAsync(_factory, s));
        Assert.Equal(TaskItemStatus.Completed, task.Status);

        var changed = await GetEmployeeAsync(_factory, s.EmployeeId);
        Assert.Equal(s.World.Target.PositionProfileId, changed.PositionProfileId);
        Assert.Equal(s.World.Target.DepartmentId, changed.DepartmentId);
        Assert.Equal(s.World.Target.LocationId, changed.LocationId);
        Assert.Equal(s.World.NewManagerId, changed.ManagerId);

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(OfferResponseStatus.Accepted, application.OfferResponseStatus);
        Assert.Equal(InternalAppointmentStatus.Completed, application.AppointmentStatus);
        Assert.Equal(recruiterId, application.OfferMadeByUserId);
    }

    [Fact]
    public async Task Appoint_Before_Any_Offer_Is_Made_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), new { effectiveDate = Today.ToString("yyyy-MM-dd"), noManager = true });

        await AssertBlockedAsync(s, response);
    }

    [Fact]
    public async Task Appoint_While_The_Offer_Is_Awaiting_Response_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter, StartsToday);

        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), new { });

        await AssertBlockedAsync(s, response);
    }

    [Fact]
    public async Task Appoint_After_The_Employee_Declined_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter, StartsToday);
        using var employee = await EmployeeClientAsync(_factory, s);
        await RespondOkAsync(employee, s, "Decline", reason: "No thanks");

        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), new { });

        await AssertBlockedAsync(s, response);
    }

    [Fact]
    public async Task Appoint_After_The_Offer_Was_Withdrawn_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter, StartsToday);
        await AssertStatusAsync(HttpStatusCode.OK, await RecruiterRespondAsync(recruiter, s, "Withdrawn"));

        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), new { });

        await AssertBlockedAsync(s, response);
    }

    [Fact]
    public async Task Appoint_After_A_Recruiter_Recorded_The_Acceptance_Is_Allowed()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter, StartsToday);
        await AssertStatusAsync(HttpStatusCode.OK, await RecruiterRespondAsync(recruiter, s, "Accepted"));

        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), new { });

        await AssertStatusAsync(HttpStatusCode.OK, response);
    }

    [Fact]
    public async Task Appoint_Defaults_To_The_Accepted_Terms_And_Leaves_Identity_Untouched()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, recruiterId) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter, StartsToday);
        using var employeeClient = await EmployeeClientAsync(_factory, s);
        await RespondOkAsync(employeeClient, s, "Accept");
        var employeesBefore = await CountEmployeesAsync(_factory, companyId);
        var rolesBefore = await GetRoleIdsAsync(s.EmployeeId);
        var fanOutBefore = await CountNewHireFanOutAsync(companyId, s.EmployeeId);

        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), new { });

        await AssertStatusAsync(HttpStatusCode.OK, response);
        var body = await ReadAppointResponseAsync(response);
        Assert.Equal(Today, body.EffectiveDate);
        Assert.Equal(s.World.NewManagerId, body.ManagerId);
        Assert.NotNull(body.CompensationId);

        var employee = await GetEmployeeAsync(_factory, s.EmployeeId);
        Assert.Equal(s.World.NewManagerId, employee.ManagerId);
        Assert.Equal(s.World.Target.PositionProfileId, employee.PositionProfileId);
        Assert.Equal(s.World.EmployeeNumber, employee.EmployeeNumber);
        Assert.Equal(StartDate, employee.StartDate);
        Assert.Equal(ContinuousServiceDate, employee.ContinuousServiceDate);
        Assert.Equal(s.World.WorkEmail, employee.WorkEmail);
        Assert.Equal(s.World.Current.EmploymentTypeId, employee.EmploymentTypeId);

        var compensation = Assert.Single(await GetCompensationsAsync(_factory, s.EmployeeId));
        Assert.Equal(body.CompensationId, compensation.Id);
        Assert.Equal(DefaultSalary, compensation.Salary);
        Assert.Equal(SalaryType.Annual, compensation.SalaryType);
        Assert.Equal(DefaultCurrency, compensation.Currency);
        Assert.Equal(37.5m, compensation.HoursPerWeek);
        Assert.Equal(1m, compensation.FTE);
        Assert.Equal(CompensationChangeReason.RoleChange, compensation.Reason);
        Assert.Equal(Today, compensation.EffectiveFrom);

        var promotion = Assert.Single(await GetPromotionsAsync(_factory, s.EmployeeId));
        Assert.Equal(Today, promotion.EffectiveDate);
        Assert.Equal(compensation.Id, promotion.CompensationId);
        Assert.Equal(s.SourceReference, promotion.SourceReference);

        Assert.Equal(employeesBefore, await CountEmployeesAsync(_factory, companyId));
        Assert.Equal(rolesBefore, await GetRoleIdsAsync(s.EmployeeId));
        Assert.Equal(fanOutBefore, await CountNewHireFanOutAsync(companyId, s.EmployeeId));
        Assert.Equal(recruiterId, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferMadeByUserId);
    }

    [Fact]
    public async Task Appoint_With_An_Offer_Of_No_Manager_Clears_The_Manager()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter, b =>
        {
            StartsToday(b);
            b.Remove("proposedManagerId");
            b["noManager"] = true;
        });
        using var employeeClient = await EmployeeClientAsync(_factory, s);
        await RespondOkAsync(employeeClient, s, "Accept");

        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), new { });

        await AssertStatusAsync(HttpStatusCode.OK, response);
        Assert.Null((await ReadAppointResponseAsync(response)).ManagerId);
        Assert.Null((await GetEmployeeAsync(_factory, s.EmployeeId)).ManagerId);
        Assert.True(Assert.Single(await GetPromotionsAsync(_factory, s.EmployeeId)).ClearsManager);
    }

    [Fact]
    public async Task Appoint_Accepts_Explicit_Values_That_Match_The_Accepted_Terms()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter, StartsToday);
        using var employeeClient = await EmployeeClientAsync(_factory, s);
        await RespondOkAsync(employeeClient, s, "Accept");

        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), new
        {
            effectiveDate = Today.ToString("yyyy-MM-dd"),
            managerId = s.World.NewManagerId,
            noManager = false,
            createCompensationChange = true,
            compensationSalaryType = "Annual",
            compensationSalary = DefaultSalary,
            compensationCurrency = "gbp",
            compensationHoursPerWeek = 37.5m,
            compensationFte = 1m,
        });

        await AssertStatusAsync(HttpStatusCode.OK, response);
        Assert.Single(await GetCompensationsAsync(_factory, s.EmployeeId));
    }

    public static TheoryData<string, string> MaterialChanges => new()
    {
        { "effective date", """{ "effectiveDate": "{{DATE+1}}" }""" },
        { "different manager", """{ "managerId": "{{OLDMANAGER}}", "noManager": false }""" },
        { "no manager", """{ "noManager": true }""" },
        { "salary", """{ "createCompensationChange": true, "compensationSalaryType": "Annual", "compensationSalary": 80000, "compensationCurrency": "GBP" }""" },
        { "currency", """{ "createCompensationChange": true, "compensationSalaryType": "Annual", "compensationSalary": 75000, "compensationCurrency": "EUR" }""" },
        { "frequency", """{ "createCompensationChange": true, "compensationSalaryType": "Hourly", "compensationSalary": 75000, "compensationCurrency": "GBP" }""" },
        { "hours per week", """{ "createCompensationChange": true, "compensationSalaryType": "Annual", "compensationSalary": 75000, "compensationCurrency": "GBP", "compensationHoursPerWeek": 30 }""" },
        { "fte", """{ "createCompensationChange": true, "compensationSalaryType": "Annual", "compensationSalary": 75000, "compensationCurrency": "GBP", "compensationFte": 0.8 }""" },
    };

    [Theory]
    [MemberData(nameof(MaterialChanges))]
    public async Task Appoint_With_A_Changed_Material_Term_Returns_BadRequest_Asking_For_A_Revised_Offer(string term, string template)
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter, StartsToday);
        using var employeeClient = await EmployeeClientAsync(_factory, s);
        await RespondOkAsync(employeeClient, s, "Accept");
        var json = template
            .Replace("{{DATE+1}}", Today.AddDays(1).ToString("yyyy-MM-dd"))
            .Replace("{{OLDMANAGER}}", s.World.OldManagerId.ToString());
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        var response = await recruiter.PostAsync(AppointUrl(s), content);

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        var problem = await response.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.Equal("validation", problem!.Code);
        Assert.Contains("revised offer", problem.Error, StringComparison.OrdinalIgnoreCase);
        await AssertNotAppointedAsync(s);
    }

    [Fact]
    public async Task Appoint_With_A_Manager_When_The_Offer_Was_No_Manager_Returns_BadRequest_Asking_For_A_Revised_Offer()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter, b =>
        {
            StartsToday(b);
            b.Remove("proposedManagerId");
            b["noManager"] = true;
        });
        using var employeeClient = await EmployeeClientAsync(_factory, s);
        await RespondOkAsync(employeeClient, s, "Accept");

        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), new { managerId = s.World.NewManagerId, noManager = false });

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        Assert.Contains("revised offer", (await response.Content.ReadFromJsonAsync<ProblemPayload>())!.Error, StringComparison.OrdinalIgnoreCase);
        await AssertNotAppointedAsync(s);
    }

    [Fact]
    public async Task Appoint_With_A_Future_Dated_Accepted_Offer_Is_Scheduled_And_Leaves_The_Employee_Unchanged()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var start = Today.AddDays(30);
        var s = await SeedOfferedAsync(_factory, companyId, recruiter, b => b["proposedStartDate"] = start.ToString("yyyy-MM-dd"));
        using var employeeClient = await EmployeeClientAsync(_factory, s);
        await RespondOkAsync(employeeClient, s, "Accept");

        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), new { });

        await AssertStatusAsync(HttpStatusCode.OK, response);
        var body = await ReadAppointResponseAsync(response);
        Assert.False(body.IsApplied);
        Assert.Equal(start, body.EffectiveDate);

        var employee = await GetEmployeeAsync(_factory, s.EmployeeId);
        Assert.Equal(s.World.Current.PositionProfileId, employee.PositionProfileId);
        Assert.Equal(s.World.Current.DepartmentId, employee.DepartmentId);
        Assert.Equal(s.World.Current.LocationId, employee.LocationId);
        Assert.Equal(s.World.OldManagerId, employee.ManagerId);

        var promotion = Assert.Single(await GetPromotionsAsync(_factory, s.EmployeeId));
        Assert.Null(promotion.CompletedAt);
        Assert.Equal(start, promotion.EffectiveDate);

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(InternalAppointmentStatus.Completed, application.AppointmentStatus);
        Assert.Equal(start, application.AppointmentEffectiveDate);
    }

    [Fact]
    public async Task A_Revised_Offer_After_Acceptance_Blocks_Appointment_Until_Accepted_Again_And_Then_Uses_The_New_Terms()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter, StartsToday);
        using var employeeClient = await EmployeeClientAsync(_factory, s);
        await RespondOkAsync(employeeClient, s, "Accept");

        await MakeOfferOkAsync(recruiter, s, b =>
        {
            StartsToday(b);
            b["offeredSalary"] = 82000m;
        });
        var blocked = await recruiter.PostAsJsonAsync(AppointUrl(s), new { });
        await AssertBlockedAsync(s, blocked);

        await RespondOkAsync(employeeClient, s, "Accept", offerVersion: 2);
        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), new { });

        await AssertStatusAsync(HttpStatusCode.OK, response);
        Assert.Equal(82000m, Assert.Single(await GetCompensationsAsync(_factory, s.EmployeeId)).Salary);
    }

    [Fact]
    public async Task A_Legacy_Accepted_Offer_Without_A_Snapshot_Is_Appointed_With_An_Explicit_Manager_And_Date()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee, companyId);
        var s = await SeedAsync(_factory, companyId, employeeId: employeeUserId);
        using var recruiter = await RecruiterHrClientAsync(_factory, companyId);
        using var employeeClient = await EmployeeClientAsync(_factory, s);

        var viewed = await GetOfferOkAsync(employeeClient, s);
        Assert.False(viewed.GetProperty("terms").GetProperty("isSnapshotComplete").GetBoolean());

        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), AppointBody(s));

        await AssertStatusAsync(HttpStatusCode.OK, response);
        var body = await ReadAppointResponseAsync(response);
        Assert.Equal(Today, body.EffectiveDate);
        Assert.Equal(s.World.NewManagerId, body.ManagerId);
        Assert.Null(body.CompensationId);
        Assert.Equal(s.World.NewManagerId, (await GetEmployeeAsync(_factory, s.EmployeeId)).ManagerId);
        Assert.Equal(InternalAppointmentStatus.Completed, (await GetApplicationAsync(_factory, s.ApplicationId)).AppointmentStatus);
    }

    [Fact]
    public async Task A_Legacy_Accepted_Offer_Cannot_Be_Appointed_Without_A_Manager_Decision()
    {
        var companyId = Guid.NewGuid();
        using var recruiter = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);

        var response = await recruiter.PostAsJsonAsync(AppointUrl(s), new { effectiveDate = Today.ToString("yyyy-MM-dd") });

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        await AssertNotAppointedAsync(s);
    }

    private async Task AssertBlockedAsync(Scenario s, HttpResponseMessage response)
    {
        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        Assert.Equal("validation", (await response.Content.ReadFromJsonAsync<ProblemPayload>())!.Code);
        await AssertNotAppointedAsync(s);
    }

    private async Task AssertNotAppointedAsync(Scenario s)
    {
        var employee = await GetEmployeeAsync(_factory, s.EmployeeId);
        Assert.Equal(s.World.Current.PositionProfileId, employee.PositionProfileId);
        Assert.Equal(s.World.Current.DepartmentId, employee.DepartmentId);
        Assert.Equal(s.World.OldManagerId, employee.ManagerId);
        Assert.Empty(await GetPromotionsAsync(_factory, s.EmployeeId));
        Assert.Empty(await GetCompensationsAsync(_factory, s.EmployeeId));

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Null(application.AppointmentStatus);
        Assert.NotEqual(s.HiredStageId, application.CurrentStageId);
    }

    private async Task<HashSet<Guid>> GetRoleIdsAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return (await db.UserRoles.AsNoTracking().Where(r => r.UserId == userId).Select(r => r.RoleId).ToListAsync()).ToHashSet();
    }

    private async Task<(int Onboarding, int Probation, int Leave, int HiredTasks)> CountNewHireFanOutAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        return (
            await sp.GetRequiredService<OnboardingDbContext>().OnboardingPlans.CountAsync(p => p.CompanyId == companyId && p.EmployeeId == employeeId),
            await sp.GetRequiredService<ProbationDbContext>().ProbationRecords.CountAsync(p => p.CompanyId == companyId && p.EmployeeId == employeeId),
            await sp.GetRequiredService<LeaveDbContext>().EmployeeLeavePolicyAssignments.CountAsync(a => a.CompanyId == companyId && a.EmployeeId == employeeId),
            await sp.GetRequiredService<HR.Modules.Tasks.Persistence.TasksDbContext>().TaskItems.CountAsync(t => t.CompanyId == companyId && t.Title.StartsWith("Candidate hired")));
    }
}
