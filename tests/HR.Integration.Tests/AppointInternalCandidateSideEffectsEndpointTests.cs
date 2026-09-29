using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Notifications.Persistence;
using HR.Modules.Onboarding.Persistence;
using HR.Modules.Probation.Persistence;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HR.Integration.Tests.Infrastructure.InternalAppointmentTestSeeder;

namespace HR.Integration.Tests;

/// <summary>
/// Internal recruitment Ticket 7: an internal appointment changes an existing employee's role, so none
/// of the new-hire fan-out that CandidateHired / EmployeeCreated trigger may run — no onboarding plan,
/// probation record, leave initialisation, "Candidate hired" HR task, new-hire notification or
/// invitation. The external Hire path must keep working unchanged, and Hire must refuse an internal
/// application (which would otherwise create a duplicate Employee).
///
/// The positive control (<see cref="External_Hire_Still_Provisions_A_New_Employee_And_Its_Hr_Task"/>)
/// proves the same assertions DO observe those side effects for an external hire, so the
/// "nothing created" assertions for the appointment are meaningful.
/// </summary>
[Collection("Integration")]
public class AppointInternalCandidateSideEffectsEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public AppointInternalCandidateSideEffectsEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record NewHireFanOut(
        int OnboardingPlans,
        int ProbationRecords,
        int LeavePolicyAssignments,
        int CandidateHiredTasks,
        int NewHireNotifications,
        int InvitationEmails);

    private async Task<NewHireFanOut> CountNewHireFanOutAsync(Guid companyId, Guid employeeId, Guid applicationId, string workEmail)
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        var onboarding = await sp.GetRequiredService<OnboardingDbContext>().OnboardingPlans
            .CountAsync(p => p.CompanyId == companyId && p.EmployeeId == employeeId);
        var probation = await sp.GetRequiredService<ProbationDbContext>().ProbationRecords
            .CountAsync(p => p.CompanyId == companyId && p.EmployeeId == employeeId);
        var leave = await sp.GetRequiredService<LeaveDbContext>().EmployeeLeavePolicyAssignments
            .CountAsync(a => a.CompanyId == companyId && a.EmployeeId == employeeId);
        var tasks = await sp.GetRequiredService<TasksDbContext>().TaskItems
            .CountAsync(t => t.CompanyId == companyId && (t.SourceEntityId == applicationId || t.Title.StartsWith("Candidate hired")));
        var notifications = await sp.GetRequiredService<NotificationsDbContext>().Notifications
            .CountAsync(n => n.CompanyId == companyId &&
                             (n.Type == NotificationType.CandidateHired || n.Type == NotificationType.EmployeeCreated));
        var invitations = _factory.EmailSender.Sent.Count(e =>
            string.Equals(e.ToEmail, workEmail, StringComparison.OrdinalIgnoreCase) &&
            e.Subject == FakeInvitationEmailSender.Subject);

        return new NewHireFanOut(onboarding, probation, leave, tasks, notifications, invitations);
    }

    [Fact]
    public async Task Appointment_Creates_No_New_Hire_Side_Effects()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);
        var employeesBefore = await CountEmployeesAsync(_factory, companyId);
        var before = await CountNewHireFanOutAsync(companyId, s.EmployeeId, s.ApplicationId, s.World.WorkEmail);

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = await CountNewHireFanOutAsync(companyId, s.EmployeeId, s.ApplicationId, s.World.WorkEmail);
        Assert.Equal(new NewHireFanOut(0, 0, 0, 0, 0, 0), before);
        Assert.Equal(before, after);
        Assert.Equal(employeesBefore, await CountEmployeesAsync(_factory, companyId));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.Equal(s.EmployeeId, (await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == s.CandidateId)).EmployeeId);
    }

    [Fact]
    public async Task Scheduled_Appointment_Creates_No_New_Hire_Side_Effects()
    {
        var companyId = Guid.NewGuid();
        using var client = await RecruiterHrClientAsync(_factory, companyId);
        var s = await SeedAsync(_factory, companyId);
        var employeesBefore = await CountEmployeesAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(AppointUrl(s), AppointBody(s, effectiveDate: Today.AddDays(10)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            new NewHireFanOut(0, 0, 0, 0, 0, 0),
            await CountNewHireFanOutAsync(companyId, s.EmployeeId, s.ApplicationId, s.World.WorkEmail));
        Assert.Equal(employeesBefore, await CountEmployeesAsync(_factory, companyId));
    }

    [Fact]
    public async Task External_Hire_Still_Provisions_A_New_Employee_And_Its_Hr_Task()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientWithRolesAsync(_factory, companyId, SystemRoles.Recruiter);
        var s = await SeedAsync(_factory, companyId, source: ApplicationSource.Direct);
        var employeesBefore = await CountEmployeesAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(HireUrl(companyId, s.VacancyId, s.ApplicationId), new
        {
            startDate = Today.AddDays(14).ToString("yyyy-MM-dd"),
            dateOfBirth = new DateOnly(1994, 5, 17).ToString("yyyy-MM-dd"),
            nationality = "British",
            gender = "Prefer not to say",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            employmentTypeId = s.World.Target.EmploymentTypeId,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newEmployeeId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("employeeId").GetGuid();
        Assert.NotEqual(s.EmployeeId, newEmployeeId);
        Assert.Equal(employeesBefore + 1, await CountEmployeesAsync(_factory, companyId));

        var fanOut = await CountNewHireFanOutAsync(companyId, newEmployeeId, s.ApplicationId, "unused@example.com");
        Assert.Equal(1, fanOut.CandidateHiredTasks);

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(s.HiredStageId, application.CurrentStageId);
        Assert.Null(application.AppointmentStatus);

        Assert.Empty(await GetPromotionsAsync(_factory, s.EmployeeId));
    }

    [Fact]
    public async Task Hire_On_Internal_Application_Returns_BadRequest_Without_Creating_An_Employee()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientWithRolesAsync(_factory, companyId, SystemRoles.Recruiter);
        var s = await SeedAsync(_factory, companyId);
        var employeesBefore = await CountEmployeesAsync(_factory, companyId);

        var response = await client.PostAsJsonAsync(HireUrl(companyId, s.VacancyId, s.ApplicationId), new
        {
            startDate = Today.AddDays(14).ToString("yyyy-MM-dd"),
            dateOfBirth = new DateOnly(1990, 1, 1).ToString("yyyy-MM-dd"),
            nationality = "British",
            gender = "Prefer not to say",
            employeeNumber = $"EMP-{Guid.NewGuid():N}",
            employmentTypeId = s.World.Target.EmploymentTypeId,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation", (await response.Content.ReadFromJsonAsync<ProblemPayload>())!.Code);
        Assert.Equal(employeesBefore, await CountEmployeesAsync(_factory, companyId));
        Assert.Equal(
            new NewHireFanOut(0, 0, 0, 0, 0, 0),
            await CountNewHireFanOutAsync(companyId, s.EmployeeId, s.ApplicationId, s.World.WorkEmail));

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(s.OfferStageId, application.CurrentStageId);
        Assert.Null(application.AppointmentStatus);
        Assert.Single(await GetStageHistoryAsync(_factory, s.ApplicationId));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        Assert.Equal(s.EmployeeId, (await db.Candidates.AsNoTracking().SingleAsync(c => c.Id == s.CandidateId)).EmployeeId);
    }
}
