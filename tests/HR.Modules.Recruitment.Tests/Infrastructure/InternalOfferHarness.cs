using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.GetInternalOffer;
using HR.Modules.Recruitment.Features.OfferCandidate;
using HR.Modules.Recruitment.Features.RespondToInternalOffer;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class InternalOfferHarness
{
    public static readonly DateTime FixedUtcNow = new(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc);
    public static readonly DateTimeOffset Now = new(2026, 10, 6, 10, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly StartDate = new(2026, 11, 1);

    public required RecruitmentDbContext Db { get; init; }
    public required Guid CompanyId { get; init; }
    public required Guid EmployeeId { get; init; }
    public required Guid ManagerId { get; init; }
    public required Guid HiringManagerId { get; init; }
    public required Guid PositionProfileId { get; init; }
    public required Guid DepartmentId { get; init; }
    public required Guid LocationId { get; init; }
    public required Vacancy Vacancy { get; init; }
    public required Candidate Candidate { get; init; }
    public required Application Application { get; init; }
    public required RecruitmentStageTestData.SeededStages Stages { get; init; }
    public required InternalOfferWiring Wiring { get; init; }
    public FakeAuditPublisher Audit { get; } = new();
    public FakeClock Clock { get; } = new(FixedUtcNow);
    public FakeIntegrationEventPublisher Events { get; } = new();

    public static async Task<InternalOfferHarness> CreateAsync(
        RecruitmentDbContext? db = null,
        bool internalApplication = true,
        bool atOfferStage = false)
    {
        db ??= BuildInMemoryContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var hiringManagerId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now.AddDays(-30));
        var vacancy = Vacancy.Create(
            Guid.NewGuid(), companyId, positionProfileId, "Engineering Manager", null, hiringManagerId, Now.AddDays(-30),
            employmentTypeId: Guid.NewGuid());
        db.Vacancies.Add(vacancy);

        Candidate candidate;
        Application application;
        var stageId = atOfferStage ? stages.Offer.Id : stages.Interview.Id;

        if (internalApplication)
        {
            (candidate, application) = InternalApplicationTestData.AddInternal(
                db, companyId, vacancy.Id, stageId, employeeId, Now.AddDays(-10));
        }
        else
        {
            (candidate, application) = InternalApplicationTestData.AddExternal(
                db, companyId, vacancy.Id, stageId, null, Now.AddDays(-10));
        }

        db.Interviews.Add(RecruitmentStageTestData.PassedInterview(companyId, application.Id, stages.Interview.Id, Now));
        await db.SaveChangesAsync();

        var positions = new FakePositionProfileReader(
            summaries: new Dictionary<Guid, PositionProfileSummary>
            {
                [positionProfileId] = new(positionProfileId, "Profile Engineering Manager", departmentId, true, locationId, "London", "Engineering"),
            },
            employmentDefaults: new Dictionary<Guid, PositionProfileEmploymentDefaults>
            {
                [positionProfileId] = new(
                    positionProfileId, "Profile Engineering Manager", 60000m, 80000m, "Annual",
                    WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday | WorkingDays.Friday,
                    7.5m, 6, null, locationId, "London"),
            });

        var wiring = InternalOfferWiring.For(db, new FakeClock(FixedUtcNow), positions);
        wiring.Applicants.Set(FakeEmployeeApplicantReader.Profile(companyId, employeeId));
        wiring.Applicants.Set(FakeEmployeeApplicantReader.Profile(companyId, managerId, "Mo", "Manager", "mo@acme.example"));
        wiring.Names.Add(managerId, "Mo Manager");

        return new InternalOfferHarness
        {
            Db = db,
            CompanyId = companyId,
            EmployeeId = employeeId,
            ManagerId = managerId,
            HiringManagerId = hiringManagerId,
            PositionProfileId = positionProfileId,
            DepartmentId = departmentId,
            LocationId = locationId,
            Vacancy = vacancy,
            Candidate = candidate,
            Application = application,
            Stages = stages,
            Wiring = wiring,
        };
    }

    public static RecruitmentDbContext BuildInMemoryContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    public OfferCandidateHandler OfferHandler() =>
        OfferHandlerFactory.Create(
            Db, Clock, Wiring.Positions,
            new RecruitmentStageChangeRecorder(Db, Events, Audit),
            new FakeCompanyRecruitmentSettingsReader(), Audit, Wiring);

    public OfferCandidateRequest ValidOffer() => new()
    {
        CompanyId = CompanyId,
        VacancyId = Vacancy.Id,
        ApplicationId = Application.Id,
        OfferedSalary = 72000m,
        OfferedSalaryFrequency = "Annual",
        ProposedStartDate = StartDate,
        OfferDate = DateOnly.FromDateTime(Now.UtcDateTime),
        OfferNotes = "Welcome to the team.",
        ProposedManagerId = ManagerId,
        Currency = "gbp",
        Fte = 1m,
        ResponseDeadline = new DateOnly(2026, 10, 20),
    };

    public async Task<Application> OfferAsync(OfferCandidateRequest? request = null)
    {
        var result = await OfferHandler().HandleAsync(request ?? ValidOffer(), Guid.NewGuid(), CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return await ReloadAsync();
    }

    public RespondToInternalOfferHandler RespondHandler(RecruitmentDbContext? db = null, InternalOfferWiring? wiring = null)
    {
        db ??= Db;
        wiring ??= Wiring;
        return new RespondToInternalOfferHandler(db, Clock, Audit, wiring.Applicants, wiring.Effects);
    }

    public GetInternalOfferHandler GetHandler(FakePermissionAuthorizationService? authorization = null) =>
        new(Db, authorization ?? new FakePermissionAuthorizationService());

    public async Task<Application> ReloadAsync()
    {
        Db.ChangeTracker.Clear();
        return await Db.Applications.SingleAsync(a => a.Id == Application.Id);
    }

    public RespondToInternalOfferRequest Respond(string decision, int version = 1, string? reason = null) => new()
    {
        CompanyId = CompanyId,
        ApplicationId = Application.Id,
        Decision = decision,
        OfferVersion = version,
        Reason = reason,
    };
}
