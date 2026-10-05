using System.Net.Http.Json;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// Internal recruitment Ticket 7: shared seeding for the AppointInternalCandidate* integration tests.
/// Every call works against a fresh company id supplied by the test, so tests stay independent.
///
/// The seeded world:
/// <list type="bullet">
/// <item>the employee's CURRENT role (department, location, position profile) and a distinct TARGET
/// role (another department, location and position profile) that the vacancy advertises;</item>
/// <item>an Active employee on the current role with a known employee number, start date, continuous
/// service date and an existing manager, plus a second Active employee to be the new manager;</item>
/// <item>the company's default recruitment stages, an open internally advertised vacancy for the
/// target profile, and — optionally — an Internal application from that employee on the Offer stage
/// with an accepted offer, one interview and a CV Review → Offer stage-history entry.</item>
/// </list>
/// </summary>
internal static class InternalAppointmentTestSeeder
{
    public static readonly DateOnly StartDate = new(2019, 4, 1);
    public static readonly DateOnly ContinuousServiceDate = new(2018, 11, 5);

    internal sealed record EmployeeWorld(
        Guid CompanyId,
        Guid EmployeeId,
        string EmployeeNumber,
        string WorkEmail,
        Guid OldManagerId,
        Guid NewManagerId,
        EmployeeReferenceDataSeeder.ReferenceData Current,
        EmployeeReferenceDataSeeder.ReferenceData Target,
        string TargetTitle);

    internal sealed record Scenario(
        EmployeeWorld World,
        Guid VacancyId,
        Guid CandidateId,
        Guid ApplicationId,
        Guid InterviewId,
        Guid CvReviewStageId,
        Guid OfferStageId,
        Guid HiredStageId)
    {
        public Guid CompanyId => World.CompanyId;
        public Guid EmployeeId => World.EmployeeId;
        public string SourceReference => $"recruitment:application:{ApplicationId}";
    }

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static string AppointUrl(Guid companyId, Guid vacancyId, Guid applicationId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/appoint";

    public static string AppointUrl(Scenario s) => AppointUrl(s.CompanyId, s.VacancyId, s.ApplicationId);

    public static string HireUrl(Guid companyId, Guid vacancyId, Guid applicationId) =>
        $"/api/companies/{companyId}/vacancies/{vacancyId}/applications/{applicationId}/hire";

    public static Task<HttpClient> RecruiterHrClientAsync(ApiWebApplicationFactory factory, Guid companyId) =>
        ClientWithRolesAsync(factory, companyId, SystemRoles.Recruiter, SystemRoles.HrAdministrator, SystemRoles.Employee);

    public static async Task<HttpClient> ClientWithRolesAsync(ApiWebApplicationFactory factory, Guid companyId, params Guid[] roles)
    {
        var userId = Guid.NewGuid();
        foreach (var role in roles)
            await TestRoleSeeder.AssignRoleAsync(factory, userId, role, companyId);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        return client;
    }

    public static object AppointBody(
        Scenario s,
        DateOnly? effectiveDate = null,
        Guid? managerId = null,
        bool noManager = false,
        bool omitEffectiveDate = false) =>
        new
        {
            effectiveDate = omitEffectiveDate ? null : (effectiveDate ?? Today).ToString("yyyy-MM-dd"),
            managerId = noManager ? (Guid?)null : managerId ?? s.World.NewManagerId,
            noManager,
        };

    public static async Task<EmployeeWorld> SeedEmployeesAsync(
        ApiWebApplicationFactory factory, Guid companyId, Guid? employeeId = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();

        var current = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        var target = await EmployeeReferenceDataSeeder.SeedAsync(db, companyId);
        var targetTitle = await db.PositionProfiles.Where(p => p.Id == target.PositionProfileId).Select(p => p.Title).SingleAsync();
        var now = DateTimeOffset.UtcNow;

        var oldManager = NewEmployee(Guid.NewGuid(), companyId, "Olivia", "Grant", current, now);
        var newManager = NewEmployee(Guid.NewGuid(), companyId, "Nathan", "Brooks", target, now);
        db.Employees.AddRange(oldManager, newManager);
        await db.SaveChangesAsync();

        var employeeNumber = $"EMP-{Guid.NewGuid():N}"[..20];
        var employee = NewEmployee(employeeId ?? Guid.NewGuid(), companyId, "Priya", "Shah", current, now, employeeNumber);
        employee.UpdateEmploymentDetails(employeeNumber, current.EmploymentTypeId, StartDate, ContinuousServiceDate, null, null, null, now);
        employee.Assign(current.DepartmentId, current.PositionProfileId, current.LocationId, oldManager.Id, now);
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        return new EmployeeWorld(
            companyId, employee.Id, employee.EmployeeNumber, employee.WorkEmail,
            oldManager.Id, newManager.Id, current, target, targetTitle);
    }

    public static async Task<(Guid VacancyId, IReadOnlyList<RecruitmentStage> Stages)> SeedVacancyAsync(
        ApiWebApplicationFactory factory, EmployeeWorld world, string advertTitle = "Engineering Manager")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var now = DateTimeOffset.UtcNow;

        var stages = await db.RecruitmentStages.Where(s => s.CompanyId == world.CompanyId).ToListAsync();
        if (stages.Count == 0)
        {
            stages = RecruitmentStageSeeder.BuildDefaultStages(world.CompanyId, now).ToList();
            db.RecruitmentStages.AddRange(stages);
        }

        var vacancy = Vacancy.Create(
            Guid.NewGuid(), world.CompanyId, world.Target.PositionProfileId, advertTitle, "Lead the platform team.",
            world.NewManagerId, now, assignedRecruiterId: null, isAdvertisedInternally: true,
            employmentTypeId: world.Target.EmploymentTypeId);
        vacancy.Open(now, DateOnly.FromDateTime(now.UtcDateTime));
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();

        return (vacancy.Id, stages);
    }

    public static async Task<Scenario> SeedAsync(
        ApiWebApplicationFactory factory,
        Guid companyId,
        ApplicationSource source = ApplicationSource.Internal,
        DateOnly? offeredStartDate = null)
    {
        var world = await SeedEmployeesAsync(factory, companyId);
        var (vacancyId, stages) = await SeedVacancyAsync(factory, world);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var now = DateTimeOffset.UtcNow;

        var cvReview = stages.Single(s => s.Name == "CV Review");
        var offer = stages.Single(s => s.Name == "Offer");
        var hired = stages.Single(s => s.TerminalOutcome == RecruitmentStageTerminalOutcome.Hired);

        var candidate = source == ApplicationSource.Internal
            ? Candidate.CreateForEmployee(Guid.NewGuid(), companyId, world.EmployeeId, "Priya", "Shah", world.WorkEmail, null, now.AddDays(-14))
            : Candidate.Create(Guid.NewGuid(), companyId, "Emma", "Clarke", $"emma.{Guid.NewGuid():N}@example.com", null, now.AddDays(-14));

        var application = Application.Create(
            Guid.NewGuid(), companyId, vacancyId, candidate.Id, offer.Id, null, now.AddDays(-14), source);
        application.RecordOfferTerms(72000m, OfferSalaryFrequency.Annual, offeredStartDate ?? Today, Today, "Internal move.", now.AddDays(-3));
        application.RespondToOffer(OfferResponseStatus.Accepted, now.AddDays(-2));

        var interview = Interview.Create(Guid.NewGuid(), companyId, application.Id, Guid.NewGuid(), now.AddDays(-7), 45, "Room 1", now.AddDays(-10));
        interview.RecordOutcome(InterviewOutcome.Passed, "Strong candidate.", now.AddDays(-7));

        db.Candidates.Add(candidate);
        db.Applications.Add(application);
        db.Interviews.Add(interview);
        db.ApplicationStageHistoryEntries.Add(ApplicationStageHistoryEntry.Create(
            Guid.NewGuid(), companyId, application.Id, cvReview.Id, offer.Id, null, null, now.AddDays(-3)));
        await db.SaveChangesAsync();

        return new Scenario(world, vacancyId, candidate.Id, application.Id, interview.Id, cvReview.Id, offer.Id, hired.Id);
    }

    public static async Task<int> CountEmployeesAsync(ApiWebApplicationFactory factory, Guid companyId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        return await db.Employees.CountAsync(e => e.CompanyId == companyId);
    }

    public static async Task<Employee> GetEmployeeAsync(ApiWebApplicationFactory factory, Guid employeeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        return await db.Employees.AsNoTracking().SingleAsync(e => e.Id == employeeId);
    }

    public static async Task<List<EmployeePromotion>> GetPromotionsAsync(ApiWebApplicationFactory factory, Guid employeeId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();
        return await db.EmployeePromotions.AsNoTracking().Where(p => p.EmployeeId == employeeId).ToListAsync();
    }

    public static async Task<Application> GetApplicationAsync(ApiWebApplicationFactory factory, Guid applicationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.Applications.AsNoTracking().SingleAsync(a => a.Id == applicationId);
    }

    public static async Task<List<ApplicationStageHistoryEntry>> GetStageHistoryAsync(ApiWebApplicationFactory factory, Guid applicationId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        return await db.ApplicationStageHistoryEntries.AsNoTracking()
            .Where(h => h.ApplicationId == applicationId)
            .OrderBy(h => h.ChangedAt)
            .ToListAsync();
    }

    public static async Task MarkAppointmentPendingAsync(
        ApiWebApplicationFactory factory, Guid applicationId, Guid employeeId, DateTimeOffset requestedAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
        var application = await db.Applications.SingleAsync(a => a.Id == applicationId);
        application.BeginInternalAppointment(employeeId, Guid.NewGuid(), requestedAt);
        application.IncrementVersion();
        await db.SaveChangesAsync();
    }

    private static Employee NewEmployee(
        Guid id, Guid companyId, string firstName, string lastName,
        EmployeeReferenceDataSeeder.ReferenceData role, DateTimeOffset now, string? employeeNumber = null)
    {
        var employee = Employee.Create(
            id, companyId, firstName, lastName, $"{firstName.ToLowerInvariant()}.{Guid.NewGuid():N}@acme.example",
            StartDate, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            employeeNumber ?? $"EMP-{Guid.NewGuid():N}"[..20],
            role.EmploymentTypeId, role.DepartmentId, role.LocationId, role.PositionProfileId, now);
        employee.Activate(now);
        return employee;
    }

    internal sealed record AppointResponsePayload(
        Guid ApplicationId,
        Guid VacancyId,
        Guid CandidateId,
        Guid EmployeeId,
        Guid PromotionId,
        Guid CurrentStageId,
        Guid PositionProfileId,
        Guid DepartmentId,
        Guid LocationId,
        Guid? ManagerId,
        DateOnly EffectiveDate,
        bool IsApplied,
        Guid? CompensationId,
        string AppointmentStatus);

    internal sealed record ProblemPayload(string Error, string Code);

    public static async Task<AppointResponsePayload> ReadAppointResponseAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<AppointResponsePayload>())!;
}
