using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HR.Integration.Tests.Infrastructure.InternalAppointmentTestSeeder;

namespace HR.Integration.Tests;

/// <summary>
/// Internal recruitment Ticket 7: POST /api/companies/{c}/vacancies/{v}/applications/{a}/appoint —
/// completes a successful INTERNAL application by changing the existing employee's role (position
/// profile, department, location, manager) instead of creating an employee.
///
/// Status codes: request-validator failures are 422 (FastEndpoints is configured with
/// Errors.StatusCode = 422 in HR.Api); handler validation errors are 400 with code "validation";
/// not found 404; conflicts 409 — all with the <c>{ error, code }</c> body from ProblemResults.
///
/// Every test creates its own company, employees, vacancy and caller. See
/// AppointInternalCandidateHandlerTests / AppointInternalCandidateValidatorTests
/// (HR.Modules.Recruitment.Tests) and EmployeeInternalAppointmentServiceTests
/// (HR.Modules.Employees.Tests) for unit-level coverage.
/// </summary>
[Collection("Integration")]
public class AppointInternalCandidateEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public AppointInternalCandidateEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }


    [Fact]
    public async Task Post_Appoint_Changes_Existing_Employee_Role_Without_Creating_An_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);
        var employeesBefore = await CountEmployeesAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAppointResponseAsync(response);
        Assert.Equal(s.ApplicationId, body.ApplicationId);
        Assert.Equal(s.EmployeeId, body.EmployeeId);
        Assert.Equal(s.HiredStageId, body.CurrentStageId);
        Assert.Equal(s.World.Target.PositionProfileId, body.PositionProfileId);
        Assert.Equal(s.World.Target.DepartmentId, body.DepartmentId);
        Assert.Equal(s.World.Target.LocationId, body.LocationId);
        Assert.Equal(s.World.NewManagerId, body.ManagerId);
        Assert.Equal(Today, body.EffectiveDate);
        Assert.True(body.IsApplied);
        Assert.Null(body.CompensationId);
        Assert.Equal("Completed", body.AppointmentStatus);

        var employee = await GetEmployeeAsync(_factory, s.EmployeeId);
        Assert.Equal(s.World.Target.PositionProfileId, employee.PositionProfileId);
        Assert.Equal(s.World.Target.DepartmentId, employee.DepartmentId);
        Assert.Equal(s.World.Target.LocationId, employee.LocationId);
        Assert.Equal(s.World.NewManagerId, employee.ManagerId);

        Assert.Equal(s.World.EmployeeNumber, employee.EmployeeNumber);
        Assert.Equal(StartDate, employee.StartDate);
        Assert.Equal(ContinuousServiceDate, employee.ContinuousServiceDate);
        Assert.Equal(s.World.WorkEmail, employee.WorkEmail);

        Assert.Equal(employeesBefore, await CountEmployeesAsync(_factory, companyId));

        var promotion = Assert.Single(await GetPromotionsAsync(_factory, s.EmployeeId));
        Assert.Equal(body.PromotionId, promotion.Id);
        Assert.Equal(s.SourceReference, promotion.SourceReference);
        Assert.NotNull(promotion.CompletedAt);
    }

    [Fact]
    public async Task Post_Appoint_Moves_Application_To_Hired_And_Retains_Interviews_Offer_And_History()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAppointResponseAsync(response);

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(s.HiredStageId, application.CurrentStageId);
        Assert.Equal(InternalAppointmentStatus.Completed, application.AppointmentStatus);
        Assert.Equal(body.PromotionId, application.AppointmentPromotionId);
        Assert.Equal(Today, application.AppointmentEffectiveDate);
        Assert.Equal(s.EmployeeId, application.AppointmentEmployeeId);
        Assert.Equal(ApplicationSource.Internal, application.Source);
        Assert.Null(application.WithdrawnAt);

        Assert.Equal(OfferResponseStatus.Accepted, application.OfferResponseStatus);
        Assert.Equal(72000m, application.OfferedSalary);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var interview = await db.Interviews.AsNoTracking().SingleAsync(i => i.ApplicationId == s.ApplicationId);
            Assert.Equal(s.InterviewId, interview.Id);
            Assert.Equal(InterviewOutcome.Passed, interview.Outcome);
        }

        var history = await GetStageHistoryAsync(_factory, s.ApplicationId);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, h => h.PreviousStageId == s.CvReviewStageId && h.NewStageId == s.OfferStageId);
        var hiredEntry = Assert.Single(history, h => h.NewStageId == s.HiredStageId);
        Assert.Equal(s.OfferStageId, hiredEntry.PreviousStageId);

        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/companies/{companyId}/vacancies/{s.VacancyId}/applications/{s.ApplicationId}");
        Assert.Equal("Completed", detail.GetProperty("internalAppointmentStatus").GetString());
        Assert.Equal(Today.ToString("yyyy-MM-dd"), detail.GetProperty("internalAppointmentEffectiveDate").GetString());
    }

    [Fact]
    public async Task Post_Appoint_After_Employee_Applies_Through_The_Internal_Apply_Endpoint()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee, companyId);
        var world = await SeedEmployeesAsync(_factory, companyId, employeeId: employeeUserId);
        var (vacancyId, stages) = await SeedVacancyAsync(_factory, world);
        var employeesBefore = await CountEmployeesAsync(_factory, companyId);

        Guid applicationId;
        using var employeeClient = _factory.CreateClient();
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, employeeUserId.ToString());
        employeeClient.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        {
            using var form = new MultipartFormDataContent();
            var bytes = new byte[2048];
            bytes[0] = 0x25; bytes[1] = 0x50; bytes[2] = 0x44; bytes[3] = 0x46;
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
            form.Add(file, "CvFile", "priya-cv.pdf");

            var apply = await employeeClient.PostAsync(
                $"/api/companies/{companyId}/internal-vacancies/{vacancyId}/applications", form);
            Assert.True(apply.IsSuccessStatusCode, await apply.Content.ReadAsStringAsync());
            applicationId = (await apply.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("applicationId").GetGuid();
        }

        await PassedInterviewSeed.AddForAllInterviewStagesAsync(_factory, companyId, applicationId);

        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var offer = await client.PostAsJsonAsync(
            $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/offer",
            new
            {
                proposedStartDate = Today.ToString("yyyy-MM-dd"),
                offeredSalary = 70000m,
                offeredSalaryFrequency = "Annual",
                currency = "GBP",
                proposedManagerId = world.NewManagerId,
            });
        Assert.Equal(HttpStatusCode.OK, offer.StatusCode);

        var accept = await employeeClient.PostAsJsonAsync(
            $"/api/companies/{companyId}/internal-offers/{applicationId}/response",
            new { decision = "Accept", offerVersion = 1 });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);

        var response = await client.PostAsJsonAsync(
            AppointUrl(companyId, vacancyId, applicationId),
            new { effectiveDate = Today.ToString("yyyy-MM-dd"), managerId = world.NewManagerId, noManager = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var employee = await GetEmployeeAsync(_factory, world.EmployeeId);
        Assert.Equal(world.Target.PositionProfileId, employee.PositionProfileId);
        Assert.Equal(world.Target.DepartmentId, employee.DepartmentId);
        Assert.Equal(world.Target.LocationId, employee.LocationId);
        Assert.Equal(world.NewManagerId, employee.ManagerId);
        Assert.Equal(world.EmployeeNumber, employee.EmployeeNumber);
        Assert.Equal(employeesBefore, await CountEmployeesAsync(_factory, companyId));

        var hiredStageId = stages.Single(st => st.TerminalOutcome == RecruitmentStageTerminalOutcome.Hired).Id;
        Assert.Equal(hiredStageId, (await GetApplicationAsync(_factory, applicationId)).CurrentStageId);
    }

    [Fact]
    public async Task Post_Appoint_Uses_Offered_Start_Date_When_No_Effective_Date_Supplied()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var offeredStart = Today.AddDays(21);
        var s = await SeedAsync(_factory, companyId, offeredStartDate: offeredStart);

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s, omitEffectiveDate: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAppointResponseAsync(response);
        Assert.Equal(offeredStart, body.EffectiveDate);
        Assert.False(body.IsApplied);
    }

    [Fact]
    public async Task Post_Appoint_With_Compensation_Records_A_Role_Change_Compensation()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(AppointUrl(s), new
        {
            effectiveDate = Today.ToString("yyyy-MM-dd"),
            managerId = s.World.NewManagerId,
            noManager = false,
            createCompensationChange = true,
            compensationSalaryType = "Annual",
            compensationSalary = 78000m,
            compensationCurrency = "GBP",
            compensationHoursPerWeek = 37.5m,
            compensationFte = 1m,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAppointResponseAsync(response);
        Assert.NotNull(body.CompensationId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
        var compensation = await db.Compensations.AsNoTracking().SingleAsync(c => c.EmployeeId == s.EmployeeId);
        Assert.Equal(body.CompensationId, compensation.Id);
        Assert.Equal(78000m, compensation.Salary);
        Assert.Equal(HR.Modules.Employees.Domain.CompensationChangeReason.RoleChange, compensation.Reason);
        Assert.Equal(Today, compensation.EffectiveFrom);
        Assert.Equal(compensation.Id, Assert.Single(await GetPromotionsAsync(_factory, s.EmployeeId)).CompensationId);
    }


    [Fact]
    public async Task Post_Appoint_With_NoManager_Clears_The_Employee_Manager()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);
        Assert.Equal(s.World.OldManagerId, (await GetEmployeeAsync(_factory, s.EmployeeId)).ManagerId);

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s, noManager: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await ReadAppointResponseAsync(response)).ManagerId);
        Assert.Null((await GetEmployeeAsync(_factory, s.EmployeeId)).ManagerId);
        Assert.True(Assert.Single(await GetPromotionsAsync(_factory, s.EmployeeId)).ClearsManager);
    }

    [Fact]
    public async Task Post_Appoint_Future_Dated_Is_Scheduled_And_Employee_Is_Unchanged()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var effective = Today.AddDays(30);
        var s = await SeedAsync(_factory, companyId, offeredStartDate: effective);

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s, effectiveDate: effective));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAppointResponseAsync(response);
        Assert.False(body.IsApplied);
        Assert.Equal(effective, body.EffectiveDate);

        var employee = await GetEmployeeAsync(_factory, s.EmployeeId);
        Assert.Equal(s.World.Current.PositionProfileId, employee.PositionProfileId);
        Assert.Equal(s.World.Current.DepartmentId, employee.DepartmentId);
        Assert.Equal(s.World.Current.LocationId, employee.LocationId);
        Assert.Equal(s.World.OldManagerId, employee.ManagerId);

        var promotion = Assert.Single(await GetPromotionsAsync(_factory, s.EmployeeId));
        Assert.Null(promotion.CompletedAt);
        Assert.Equal(effective, promotion.EffectiveDate);

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(InternalAppointmentStatus.Completed, application.AppointmentStatus);
        Assert.Equal(s.HiredStageId, application.CurrentStageId);
        Assert.Equal(effective, application.AppointmentEffectiveDate);
    }

    [Fact]
    public async Task Post_Appoint_Backdated_Without_Confirmation_Returns_Conflict_And_Releases_Application()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId, offeredStartDate: Today.AddDays(-7));

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s, effectiveDate: Today.AddDays(-7)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("conflict", (await response.Content.ReadFromJsonAsync<ProblemPayload>())!.Code);
        Assert.Empty(await GetPromotionsAsync(_factory, s.EmployeeId));

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Null(application.AppointmentStatus);
        Assert.Equal(s.OfferStageId, application.CurrentStageId);

        var confirmed = await client.PostAsJsonAsync(AppointUrl(s), new
        {
            effectiveDate = Today.AddDays(-7).ToString("yyyy-MM-dd"),
            managerId = s.World.NewManagerId,
            noManager = false,
            confirmBackdatedEffectiveDate = true,
        });
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.True((await ReadAppointResponseAsync(confirmed)).IsApplied);
    }


    [Fact]
    public async Task Promotion_History_Shows_The_Internal_Appointment()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);
        var appoint = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));
        Assert.Equal(HttpStatusCode.OK, appoint.StatusCode);
        var promotionId = (await ReadAppointResponseAsync(appoint)).PromotionId;

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{s.EmployeeId}/promotions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToList();
        var item = Assert.Single(items);
        Assert.Equal(promotionId, item.GetProperty("id").GetGuid());
        Assert.Equal(s.World.TargetTitle, item.GetProperty("newPositionProfileTitle").GetString());
        Assert.Equal(Today.ToString("yyyy-MM-dd"), item.GetProperty("effectiveDate").GetString());
        Assert.StartsWith("Internal appointment", item.GetProperty("reason").GetString());
        Assert.NotEqual(JsonValueKind.Null, item.GetProperty("completedAt").ValueKind);
    }

    [Fact]
    public async Task Timeline_Shows_Internal_Appointment_Entry()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);
        var appoint = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));
        Assert.Equal(HttpStatusCode.OK, appoint.StatusCode);
        var promotionId = (await ReadAppointResponseAsync(appoint)).PromotionId;

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{s.EmployeeId}/timeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToList();
        var entry = Assert.Single(items, i =>
            i.GetProperty("eventType").GetString() == "EmployeePromoted" &&
            i.GetProperty("sourceRecordId").GetGuid() == promotionId);
        Assert.Equal("Internal appointment", entry.GetProperty("title").GetString());
        Assert.Contains(s.World.TargetTitle, entry.GetProperty("summary").GetString());
        Assert.DoesNotContain(items, i => i.GetProperty("title").GetString() == "Promoted");
    }

    [Fact]
    public async Task Timeline_Shows_Internal_Appointment_Entry_For_A_Scheduled_Appointment()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId, offeredStartDate: Today.AddDays(14));
        var appoint = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s, effectiveDate: Today.AddDays(14)));
        Assert.Equal(HttpStatusCode.OK, appoint.StatusCode);

        var response = await client.GetAsync($"/api/companies/{companyId}/employees/{s.EmployeeId}/timeline");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, i => i.GetProperty("title").GetString() == "Internal appointment");
    }


    [Fact]
    public async Task Post_Appoint_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            AppointUrl(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            new { effectiveDate = Today.ToString("yyyy-MM-dd"), noManager = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Appoint_Succeeds_For_Recruiter_Without_Employee_Manage()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientWithRolesAsync(_factory, companyId, SystemRoles.Recruiter);
        var s = await SeedAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAppointResponseAsync(response);
        Assert.Equal(s.EmployeeId, body.EmployeeId);
        Assert.Equal(s.HiredStageId, body.CurrentStageId);
        Assert.Equal("Completed", body.AppointmentStatus);
        Assert.True(body.IsApplied);

        var employee = await GetEmployeeAsync(_factory, s.EmployeeId);
        Assert.Equal(s.World.Target.PositionProfileId, employee.PositionProfileId);
        Assert.Equal(s.World.NewManagerId, employee.ManagerId);
        Assert.Equal(s.SourceReference, Assert.Single(await GetPromotionsAsync(_factory, s.EmployeeId)).SourceReference);
    }

    [Fact]
    public async Task Post_Appoint_With_Compensation_Succeeds_For_Recruiter_Without_Employee_Manage()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientWithRolesAsync(_factory, companyId, SystemRoles.Recruiter);
        var s = await SeedAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(AppointUrl(s), new
        {
            effectiveDate = Today.ToString("yyyy-MM-dd"),
            noManager = true,
            createCompensationChange = true,
            compensationSalaryType = "Annual",
            compensationSalary = 64000m,
            compensationCurrency = "GBP",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAppointResponseAsync(response);
        Assert.NotNull(body.CompensationId);
        Assert.Null(body.ManagerId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
        var compensation = await db.Compensations.AsNoTracking().SingleAsync(c => c.EmployeeId == s.EmployeeId);
        Assert.Equal(body.CompensationId, compensation.Id);
        Assert.Equal(64000m, compensation.Salary);
    }

    [Fact]
    public async Task Post_Appoint_Returns_Forbidden_For_HrAdministrator_Without_Recruitment_Manage()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientWithRolesAsync(_factory, companyId, SystemRoles.HrAdministrator, SystemRoles.Employee);
        var s = await SeedAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertUntouchedAsync(s);
    }


    [Fact]
    public async Task Post_Appoint_Returns_NotFound_For_Unknown_Application()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(AppointUrl(companyId, s.VacancyId, Guid.NewGuid()), AppointBody(s));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", (await response.Content.ReadFromJsonAsync<ProblemPayload>())!.Code);
        await AssertUntouchedAsync(s);
    }

    [Fact]
    public async Task Post_Appoint_Returns_NotFound_For_Another_Companys_Application()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var other = await SeedAsync(_factory, otherCompanyId);

        var response = await client.PostAsJsonAsync(
            AppointUrl(companyId, other.VacancyId, other.ApplicationId), AppointBody(other));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertUntouchedAsync(other);
    }

    [Fact]
    public async Task Post_Appoint_Returns_BadRequest_For_External_Application()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId, source: ApplicationSource.Direct);
        var employeesBefore = await CountEmployeesAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", (await response.Content.ReadFromJsonAsync<ProblemPayload>())!.Code);
        await AssertUntouchedAsync(s);
        Assert.Equal(employeesBefore, await CountEmployeesAsync(_factory, companyId));
    }

    [Fact]
    public async Task Post_Appoint_Returns_Conflict_When_Retried_After_Completion()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);
        var first = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("conflict", (await second.Content.ReadFromJsonAsync<ProblemPayload>())!.Code);
        Assert.Single(await GetPromotionsAsync(_factory, s.EmployeeId));
    }

    [Fact]
    public async Task Post_Appoint_Returns_BadRequest_When_Manager_Is_The_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s, managerId: s.EmployeeId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUntouchedAsync(s);
    }

    [Fact]
    public async Task Post_Appoint_Returns_NotFound_When_Manager_Does_Not_Exist()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s, managerId: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertUntouchedAsync(s);
    }

    [Fact]
    public async Task Post_Appoint_Returns_BadRequest_When_Employee_No_Longer_Active()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
            var employee = await db.Employees.SingleAsync(e => e.Id == s.EmployeeId);
            employee.Suspend(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertUntouchedAsync(s);
    }


    public static TheoryData<string> InvalidBodies => new()
    {
        """{ "effectiveDate": "2026-10-01", "managerId": "3f2b8c1e-9d4a-4c55-8e1f-0a6b7c8d9e10", "noManager": true }""",
        """{ "effectiveDate": "2026-10-01", "noManager": true, "createCompensationChange": true }""",
        """{ "effectiveDate": "2026-10-01", "noManager": true, "createCompensationChange": true, "compensationSalaryType": "Weekly", "compensationSalary": 50000, "compensationCurrency": "GBP" }""",
        """{ "effectiveDate": "2026-10-01", "noManager": true, "createCompensationChange": true, "compensationSalaryType": "Annual", "compensationSalary": 0, "compensationCurrency": "GBP" }""",
        """{ "effectiveDate": "2026-10-01", "noManager": true, "createCompensationChange": true, "compensationSalaryType": "Annual", "compensationSalary": 50000, "compensationCurrency": "POUNDS" }""",
        """{ "effectiveDate": "2026-10-01", "noManager": true, "createCompensationChange": true, "compensationSalaryType": "Annual", "compensationSalary": 50000, "compensationCurrency": "GBP", "compensationFte": 1.5 }""",
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Post_Appoint_Returns_422_For_Invalid_Request(string json)
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);

        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync(AppointUrl(s), content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertUntouchedAsync(s);
    }


    [Fact]
    public async Task Post_Appoint_Returns_BadRequest_For_Legacy_Offer_When_Neither_Manager_Nor_NoManager_Is_Supplied()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(AppointUrl(s), new
        {
            effectiveDate = Today.ToString("yyyy-MM-dd"),
            noManager = false,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", (await response.Content.ReadFromJsonAsync<ProblemPayload>())!.Code);
        await AssertUntouchedAsync(s);
    }

    private async Task AssertUntouchedAsync(Scenario s)
    {
        var employee = await GetEmployeeAsync(_factory, s.EmployeeId);
        Assert.Equal(s.World.Current.PositionProfileId, employee.PositionProfileId);
        Assert.Equal(s.World.Current.DepartmentId, employee.DepartmentId);
        Assert.Equal(s.World.OldManagerId, employee.ManagerId);
        Assert.Empty(await GetPromotionsAsync(_factory, s.EmployeeId));

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Null(application.AppointmentStatus);
        Assert.Equal(s.OfferStageId, application.CurrentStageId);
        Assert.Single(await GetStageHistoryAsync(_factory, s.ApplicationId));
    }
}
