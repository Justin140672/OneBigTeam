using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.AppointInternalCandidate;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

/// <summary>
/// Internal recruitment Ticket 7: <see cref="AppointInternalCandidateHandler"/> completes an internal
/// application by changing the existing employee's role through
/// <see cref="IEmployeeInternalAppointmentService"/> — never by provisioning a new employee.
/// </summary>
public class AppointInternalCandidateHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 26, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly EffectiveDate = new(2026, 10, 1);

    private sealed class Harness
    {
        public required RecruitmentDbContext Db { get; init; }
        public required Guid CompanyId { get; init; }
        public required Vacancy Vacancy { get; init; }
        public required Candidate Candidate { get; init; }
        public required Application Application { get; init; }
        public required Guid EmployeeId { get; init; }
        public required RecruitmentStageTestData.SeededStages Stages { get; init; }
        public required Guid DepartmentId { get; init; }
        public required Guid LocationId { get; init; }
        public FakeEmployeeApplicantReader ApplicantReader { get; set; } = new();
        public FakePositionProfileReader ProfileReader { get; set; } = new();
        public FakeEmployeeInternalAppointmentService Service { get; } = new();
        public FakeIntegrationEventPublisher Events { get; } = new();
        public FakeAuditPublisher Audit { get; } = new();

        public AppointInternalCandidateHandler Handler()
        {
            var clock = new FakeClock(FixedUtcNow);
            var recorder = new RecruitmentStageChangeRecorder(Db, Events, Audit);
            var completer = new InternalAppointmentCompleter(Db, clock, Events, Audit, recorder);
            return new AppointInternalCandidateHandler(
                Db, ApplicantReader, ProfileReader, Service, completer, clock,
                NullLogger<AppointInternalCandidateHandler>.Instance);
        }

        public AppointInternalCandidateRequest Request() => new()
        {
            CompanyId     = CompanyId,
            VacancyId     = Vacancy.Id,
            ApplicationId = Application.Id,
            EffectiveDate = EffectiveDate,
            ManagerId     = Guid.NewGuid(),
        };

        public async Task<Application> ReloadApplicationAsync()
        {
            Db.ChangeTracker.Clear();
            return await Db.Applications.SingleAsync(a => a.Id == Application.Id);
        }
    }

    /// <summary>
    /// Seeds an internal application (employee-linked candidate, Source Internal) on the Offer stage,
    /// an active employee, and a resolvable position profile with department and location.
    /// <paramref name="configure"/> runs before the save so it can shape the application.
    /// </summary>
    private static async Task<Harness> SeedAsync(
        Func<RecruitmentStageTestData.SeededStages, RecruitmentStage>? stage = null,
        Action<Application, Harness>? configure = null,
        EmployeeApplicantEmploymentState employmentState = EmployeeApplicantEmploymentState.Active,
        bool linkCandidateToEmployee = true,
        ApplicationSource? source = ApplicationSource.Internal,
        string? advertTitle = "Engineering Manager")
    {
        var db = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var locationId = Guid.NewGuid();

        var stages = RecruitmentStageTestData.AddDefaultStages(db, companyId, Now.AddDays(-30));
        var vacancy = Vacancy.Create(Guid.NewGuid(), companyId, positionProfileId, advertTitle, null, Guid.NewGuid(), Now.AddDays(-30));
        db.Vacancies.Add(vacancy);

        var candidate = linkCandidateToEmployee
            ? Candidate.CreateForEmployee(Guid.NewGuid(), companyId, employeeId, "Priya", "Shah", $"priya.{Guid.NewGuid():N}@acme.example", null, Now.AddDays(-20))
            : Candidate.Create(Guid.NewGuid(), companyId, "Priya", "Shah", $"priya.{Guid.NewGuid():N}@acme.example", null, null, Now.AddDays(-20));
        var application = Application.Create(
            Guid.NewGuid(), companyId, vacancy.Id, candidate.Id,
            (stage ?? (s => s.Offer))(stages).Id, null, Now.AddDays(-20), source);
        db.Candidates.Add(candidate);
        db.Applications.Add(application);

        var harness = new Harness
        {
            Db = db,
            CompanyId = companyId,
            Vacancy = vacancy,
            Candidate = candidate,
            Application = application,
            EmployeeId = employeeId,
            Stages = stages,
            DepartmentId = departmentId,
            LocationId = locationId,
            ApplicantReader = new FakeEmployeeApplicantReader(
                FakeEmployeeApplicantReader.Profile(companyId, employeeId, state: employmentState)),
            ProfileReader = new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>
            {
                [positionProfileId] = new(positionProfileId, "Engineering Manager", departmentId, null, true, locationId, "London"),
            }),
        };

        configure?.Invoke(application, harness);
        await db.SaveChangesAsync();
        return harness;
    }

    // ------------------------------------------------------------------ happy path

    [Fact]
    public async Task HandleAsync_Moves_Application_To_Hired_And_Marks_Appointment_Completed()
    {
        var h = await SeedAsync();

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var recorded = Assert.Single(h.Service.AppointRequests);

        var saved = await h.ReloadApplicationAsync();
        Assert.Equal(h.Stages.Hired.Id, saved.CurrentStageId);
        Assert.Equal(InternalAppointmentStatus.Completed, saved.AppointmentStatus);
        Assert.Equal(h.EmployeeId, saved.AppointmentEmployeeId);
        Assert.Equal(result.Value!.PromotionId, saved.AppointmentPromotionId);
        Assert.Equal(EffectiveDate, saved.AppointmentEffectiveDate);
        Assert.Equal(Now, saved.AppointmentCompletedAt);
        Assert.False(saved.HasInternalAppointmentInProgress);
        Assert.Null(saved.WithdrawnAt);
        Assert.Equal(ApplicationSource.Internal, saved.Source);

        Assert.Equal(h.Stages.Hired.Id, result.Value.CurrentStageId);
        Assert.Equal(h.EmployeeId, result.Value.EmployeeId);
        Assert.Equal(h.Vacancy.PositionProfileId, result.Value.PositionProfileId);
        Assert.Equal(EffectiveDate, result.Value.EffectiveDate);
        Assert.Equal("Completed", result.Value.AppointmentStatus);
        Assert.Equal(h.Application.Id, result.Value.ApplicationId);
        Assert.Equal(h.Candidate.Id, result.Value.CandidateId);
        Assert.Equal(recorded.NewManagerId, result.Value.ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Sends_Employees_Module_The_Vacancy_Role_And_Stable_Source_Reference()
    {
        var h = await SeedAsync();
        var performedBy = Guid.NewGuid();
        var managerId = Guid.NewGuid();

        var result = await h.Handler().HandleAsync(h.Request() with { ManagerId = managerId }, performedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(h.Service.AppointRequests);
        Assert.Equal(h.CompanyId, request.CompanyId);
        Assert.Equal(h.EmployeeId, request.EmployeeId);
        Assert.Equal(h.Vacancy.PositionProfileId, request.NewPositionProfileId);
        Assert.Equal(EffectiveDate, request.EffectiveDate);
        Assert.Equal(managerId, request.NewManagerId);
        Assert.Equal($"recruitment:application:{h.Application.Id}", request.SourceReference);
        Assert.Equal(h.Application.InternalAppointmentSourceReference, request.SourceReference);
        Assert.Equal("Internal appointment: Engineering Manager", request.Reason);
        Assert.Equal(performedBy, request.PerformedByUserId);
        Assert.False(request.ConfirmBackdatedEffectiveDate);
        Assert.Null(request.Compensation);
    }

    [Fact]
    public async Task HandleAsync_Uses_Position_Profile_Title_In_Reason_When_Vacancy_Has_No_Advert_Title()
    {
        var h = await SeedAsync(advertTitle: null);

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Internal appointment: Engineering Manager", Assert.Single(h.Service.AppointRequests).Reason);
    }

    [Fact]
    public async Task HandleAsync_Passes_ConfirmBackdatedEffectiveDate_Through()
    {
        var h = await SeedAsync();

        var result = await h.Handler().HandleAsync(
            h.Request() with { EffectiveDate = new DateOnly(2026, 9, 1), ConfirmBackdatedEffectiveDate = true },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(h.Service.AppointRequests).ConfirmBackdatedEffectiveDate);
    }

    [Fact]
    public async Task HandleAsync_Passes_Compensation_Change_When_Requested()
    {
        var h = await SeedAsync();

        var result = await h.Handler().HandleAsync(
            h.Request() with
            {
                CreateCompensationChange = true,
                CompensationSalaryType   = "Annual",
                CompensationSalary       = 72000m,
                CompensationCurrency     = "GBP",
                CompensationHoursPerWeek = 37.5m,
                CompensationFte          = 1m,
                CompensationNotes        = "New role salary.",
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var compensation = Assert.Single(h.Service.AppointRequests).Compensation;
        Assert.NotNull(compensation);
        Assert.Equal("Annual", compensation.SalaryType);
        Assert.Equal(72000m, compensation.Salary);
        Assert.Equal("GBP", compensation.Currency);
        Assert.Equal(37.5m, compensation.HoursPerWeek);
        Assert.Equal(1m, compensation.Fte);
        Assert.Equal("New role salary.", compensation.Notes);
        Assert.NotNull(result.Value!.CompensationId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Pass_Compensation_When_Not_Requested_Even_If_Fields_Supplied()
    {
        var h = await SeedAsync();

        var result = await h.Handler().HandleAsync(
            h.Request() with { CreateCompensationChange = false, CompensationSalaryType = "Annual", CompensationSalary = 1m, CompensationCurrency = "GBP" },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(h.Service.AppointRequests).Compensation);
        Assert.Null(result.Value!.CompensationId);
    }

    [Fact]
    public async Task HandleAsync_Adds_Exactly_One_Stage_History_Entry_To_Hired()
    {
        var h = await SeedAsync();
        var performedBy = Guid.NewGuid();

        var result = await h.Handler().HandleAsync(h.Request(), performedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(await h.Db.ApplicationStageHistoryEntries.ToListAsync());
        Assert.Equal(h.Application.Id, entry.ApplicationId);
        Assert.Equal(h.Stages.Offer.Id, entry.PreviousStageId);
        Assert.Equal(h.Stages.Hired.Id, entry.NewStageId);
        Assert.Equal(performedBy, entry.ChangedByUserId);
        Assert.Equal("Internal appointment completed", entry.Notes);
    }

    [Fact]
    public async Task HandleAsync_Publishes_InternalCandidateAppointed_And_Never_CandidateHired()
    {
        var h = await SeedAsync();

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var appointed = Assert.Single(h.Events.PublishedEvents.OfType<InternalCandidateAppointedIntegrationEvent>());
        Assert.Equal(h.CompanyId, appointed.CompanyId);
        Assert.Equal(h.Application.Id, appointed.ApplicationId);
        Assert.Equal(h.Candidate.Id, appointed.CandidateId);
        Assert.Equal(h.EmployeeId, appointed.EmployeeId);
        Assert.Equal(h.Vacancy.Id, appointed.VacancyId);
        Assert.Equal(result.Value!.PromotionId, appointed.PromotionId);
        Assert.Equal(EffectiveDate, appointed.EffectiveDate);
        Assert.True(appointed.IsApplied);

        Assert.Empty(h.Events.PublishedEvents.OfType<CandidateHiredIntegrationEvent>());

        var stageChanged = Assert.Single(h.Events.PublishedEvents.OfType<ApplicationStageChangedIntegrationEvent>());
        Assert.Equal("Offer", stageChanged.PreviousStage);
        Assert.Equal("Hired", stageChanged.NewStage);
    }

    [Fact]
    public async Task HandleAsync_Publishes_InternalCandidateAppointed_Audit_Event_And_No_CandidateHired_Audit()
    {
        var h = await SeedAsync();
        var performedBy = Guid.NewGuid();

        var result = await h.Handler().HandleAsync(h.Request(), performedBy, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var audit = Assert.Single(h.Audit.Published.OfType<InternalCandidateAppointedAuditEvent>());
        Assert.Equal("candidate.internally_appointed", ((IAuditEvent)audit).EventType);
        Assert.Equal(h.Application.Id, ((IAuditEvent)audit).EntityId);
        Assert.Equal(h.EmployeeId, audit.EmployeeId);
        Assert.Equal(result.Value!.PromotionId, audit.PromotionId);
        Assert.Equal(performedBy, audit.PerformedBy);
        Assert.Empty(h.Audit.Published.OfType<CandidateHiredAuditEvent>());
    }

    [Fact]
    public async Task HandleAsync_Reports_Scheduled_When_Employees_Module_Has_Not_Applied_A_Future_Change()
    {
        var h = await SeedAsync();
        h.Service.Today = new DateOnly(2026, 9, 26);

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsApplied);
        Assert.False(Assert.Single(h.Events.PublishedEvents.OfType<InternalCandidateAppointedIntegrationEvent>()).IsApplied);

        // Still completed on the Recruitment side and counted as a hire.
        var saved = await h.ReloadApplicationAsync();
        Assert.Equal(InternalAppointmentStatus.Completed, saved.AppointmentStatus);
        Assert.Equal(h.Stages.Hired.Id, saved.CurrentStageId);
    }

    [Fact]
    public void Handler_Has_No_Dependency_On_Employee_Provisioning()
    {
        // The only way to create an employee from recruitment is IEmployeeProvisioningService; the
        // handler (and the completer it shares with the reconciliation job) must not even be able to call it.
        var handlerDependencies = typeof(AppointInternalCandidateHandler).GetConstructors()
            .SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);
        var completerDependencies = typeof(InternalAppointmentCompleter).GetConstructors()
            .SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);

        Assert.DoesNotContain(typeof(IEmployeeProvisioningService), handlerDependencies);
        Assert.DoesNotContain(typeof(IEmployeeProvisioningService), completerDependencies);
    }

    // ------------------------------------------------------------------ manager / effective date

    [Fact]
    public async Task HandleAsync_NoManager_Passes_Null_Manager()
    {
        var h = await SeedAsync();

        var result = await h.Handler().HandleAsync(
            h.Request() with { ManagerId = null, NoManager = true }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(h.Service.AppointRequests).NewManagerId);
        Assert.Null(result.Value!.ManagerId);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_Offered_Start_Date_When_No_Effective_Date_Supplied()
    {
        var offeredStart = new DateOnly(2026, 11, 2);
        var h = await SeedAsync(configure: (a, _) =>
            a.RecordOfferTerms(70000m, OfferSalaryFrequency.Annual, offeredStart, new DateOnly(2026, 9, 20), null, Now.AddDays(-6)));

        var result = await h.Handler().HandleAsync(
            h.Request() with { EffectiveDate = null }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(offeredStart, Assert.Single(h.Service.AppointRequests).EffectiveDate);
        Assert.Equal(offeredStart, (await h.ReloadApplicationAsync()).AppointmentEffectiveDate);
    }

    [Fact]
    public async Task HandleAsync_Prefers_Supplied_Effective_Date_Over_Offered_Start_Date()
    {
        var h = await SeedAsync(configure: (a, _) =>
            a.RecordOfferTerms(70000m, OfferSalaryFrequency.Annual, new DateOnly(2026, 11, 2), new DateOnly(2026, 9, 20), null, Now.AddDays(-6)));

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EffectiveDate, Assert.Single(h.Service.AppointRequests).EffectiveDate);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_No_Effective_Date_Anywhere()
    {
        var h = await SeedAsync();

        var result = await h.Handler().HandleAsync(
            h.Request() with { EffectiveDate = null }, Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "validation");
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Manager_Is_The_Employee()
    {
        var h = await SeedAsync();

        var result = await h.Handler().HandleAsync(
            h.Request() with { ManagerId = h.EmployeeId }, Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "validation");
    }

    // ------------------------------------------------------------------ eligibility

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Missing()
    {
        var h = await SeedAsync();

        var result = await h.Handler().HandleAsync(
            h.Request() with { VacancyId = Guid.NewGuid() }, Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "not_found");
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Application_Missing()
    {
        var h = await SeedAsync();

        var result = await h.Handler().HandleAsync(
            h.Request() with { ApplicationId = Guid.NewGuid() }, Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "not_found");
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Vacancy_Belongs_To_Another_Company()
    {
        var h = await SeedAsync();

        var result = await h.Handler().HandleAsync(
            h.Request() with { CompanyId = Guid.NewGuid() }, Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "not_found");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Direct")]
    [InlineData("Referral")]
    [InlineData("ExternalRecruiter")]
    public async Task HandleAsync_Returns_Validation_For_Non_Internal_Application(string? source)
    {
        var h = await SeedAsync(source: InternalApplicationTestData.ParseSource(source));

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "validation");
    }

    [Fact]
    public async Task HandleAsync_Returns_Conflict_When_Already_Completed()
    {
        var h = await SeedAsync(configure: (a, _) =>
        {
            a.BeginInternalAppointment(Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-1));
        });
        // Complete it (Hired stage) as a first appointment would have.
        h.Application.CompleteInternalAppointment(h.Stages.Hired.Id, Guid.NewGuid(), EffectiveDate, Now.AddDays(-1));
        await h.Db.SaveChangesAsync();

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Empty(h.Service.AppointRequests);
        Assert.Empty(h.Service.ResumeCalls);
        Assert.Empty(h.Events.PublishedEvents);
        Assert.Empty(await h.Db.ApplicationStageHistoryEntries.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Application_Withdrawn()
    {
        var h = await SeedAsync(configure: (a, _) => a.Withdraw(Now.AddDays(-1)));

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "validation");
    }

    [Theory]
    [InlineData("Declined")]
    [InlineData("Withdrawn")]
    public async Task HandleAsync_Returns_Validation_When_Offer_Declined_Or_Withdrawn(string response)
    {
        var h = await SeedAsync(configure: (a, _) =>
        {
            a.RecordOfferTerms(70000m, OfferSalaryFrequency.Annual, EffectiveDate, new DateOnly(2026, 9, 20), null, Now.AddDays(-6));
            a.RespondToOffer(Enum.Parse<OfferResponseStatus>(response), Now.AddDays(-2));
        });

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "validation");
    }

    [Theory]
    [InlineData("Accepted")]
    [InlineData("AwaitingResponse")]
    public async Task HandleAsync_Succeeds_When_Offer_Accepted_Or_Awaiting_Response(string response)
    {
        // An accepted offer is NOT required — same rule as the external Hire.
        var h = await SeedAsync(configure: (a, _) =>
        {
            a.RecordOfferTerms(70000m, OfferSalaryFrequency.Annual, EffectiveDate, new DateOnly(2026, 9, 20), null, Now.AddDays(-6));
            if (response != "AwaitingResponse")
                a.RespondToOffer(Enum.Parse<OfferResponseStatus>(response), Now.AddDays(-2));
        });

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public async Task HandleAsync_Succeeds_When_No_Offer_Recorded_From_A_Non_Offer_Stage()
    {
        var h = await SeedAsync(stage: s => s.Interview);

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(await h.Db.ApplicationStageHistoryEntries.ToListAsync());
        Assert.Equal(h.Stages.Interview.Id, entry.PreviousStageId);
        Assert.Equal(h.Stages.Hired.Id, entry.NewStageId);
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_On_Rejected_Terminal_Stage()
    {
        var h = await SeedAsync(stage: s => s.Rejected);

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "validation");
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Already_On_Hired_Stage_Without_Appointment()
    {
        var h = await SeedAsync(stage: s => s.Hired);

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "validation");
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_No_Active_Hired_Stage()
    {
        var h = await SeedAsync(configure: (_, harness) => harness.Stages.Hired.SetActiveStatus(false, Now));

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "validation");
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Candidate_Not_Linked_To_Employee()
    {
        var h = await SeedAsync(linkCandidateToEmployee: false);

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "validation");
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Employee_Not_Found_In_Company()
    {
        var h = await SeedAsync();
        // Profile exists only in another company: the company-scoped reader returns null.
        h.ApplicantReader = new FakeEmployeeApplicantReader(
            FakeEmployeeApplicantReader.Profile(Guid.NewGuid(), h.EmployeeId));

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "validation");
    }

    [Theory]
    [InlineData(EmployeeApplicantEmploymentState.Draft)]
    [InlineData(EmployeeApplicantEmploymentState.Suspended)]
    [InlineData(EmployeeApplicantEmploymentState.Leaving)]
    [InlineData(EmployeeApplicantEmploymentState.Former)]
    public async Task HandleAsync_Returns_Validation_When_Employee_Not_Active(EmployeeApplicantEmploymentState state)
    {
        var h = await SeedAsync(employmentState: state);

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "validation");
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Position_Profile_Missing()
    {
        var h = await SeedAsync();
        h.ProfileReader = new FakePositionProfileReader();

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "not_found");
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Position_Profile_Has_No_Department()
    {
        var h = await SeedAsync();
        var profileId = h.Vacancy.PositionProfileId;
        h.ProfileReader = new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>
        {
            [profileId] = new(profileId, "Engineering Manager", null, null, true, h.LocationId, "London"),
        });

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "validation");
    }

    [Fact]
    public async Task HandleAsync_Returns_Validation_When_Position_Profile_Has_No_Location()
    {
        var h = await SeedAsync();
        var profileId = h.Vacancy.PositionProfileId;
        h.ProfileReader = new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>
        {
            [profileId] = new(profileId, "Engineering Manager", h.DepartmentId, null, true, null, null),
        });

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        await AssertRefusedUntouchedAsync(h, result, "validation");
    }

    // ------------------------------------------------------------------ Employees-module failure

    [Theory]
    [InlineData("conflict")]
    [InlineData("validation")]
    [InlineData("not_found")]
    public async Task HandleAsync_Releases_Application_And_Returns_Error_When_Employees_Module_Refuses(string code)
    {
        var h = await SeedAsync();
        h.Service.FailWith = new Error(code, "Refused by Employees.");

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal("Refused by Employees.", result.Error.Message);
        Assert.Single(h.Service.AppointRequests);

        var saved = await h.ReloadApplicationAsync();
        Assert.Null(saved.AppointmentStatus);
        Assert.Null(saved.AppointmentEmployeeId);
        Assert.Null(saved.AppointmentRequestedByUserId);
        Assert.Null(saved.AppointmentRequestedAt);
        Assert.Null(saved.AppointmentPromotionId);
        Assert.False(saved.HasInternalAppointmentInProgress);
        Assert.Equal(h.Stages.Offer.Id, saved.CurrentStageId);

        Assert.Empty(await h.Db.ApplicationStageHistoryEntries.ToListAsync());
        Assert.Empty(h.Events.PublishedEvents);
        Assert.Empty(h.Audit.Published);
    }

    [Fact]
    public async Task HandleAsync_Can_Be_Retried_After_Employees_Module_Refused()
    {
        var h = await SeedAsync();
        h.Service.FailWith = Error.Conflict("EffectiveDate is in the past.");

        var first = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);
        Assert.True(first.IsFailure);

        h.Service.FailWith = null;
        var second = await h.Handler().HandleAsync(
            h.Request() with { ConfirmBackdatedEffectiveDate = true }, Guid.NewGuid(), CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(InternalAppointmentStatus.Completed, (await h.ReloadApplicationAsync()).AppointmentStatus);
        Assert.Equal(1, h.Service.RecordedCount);
    }

    // ------------------------------------------------------------------ recovery

    [Fact]
    public async Task HandleAsync_Completes_Pending_Appointment_From_Recorded_Change_Without_Calling_Appoint()
    {
        var originalRequester = Guid.NewGuid();
        var h = await SeedAsync(configure: (a, harness) =>
            a.BeginInternalAppointment(harness.EmployeeId, originalRequester, Now.AddMinutes(-30)));
        var recordedDate = new DateOnly(2026, 9, 28);
        var recorded = h.Service.Seed(
            h.CompanyId, h.Application.InternalAppointmentSourceReference, h.EmployeeId,
            h.Vacancy.PositionProfileId, recordedDate, newManagerId: null);

        // The employee has since changed state: recovery must not re-validate eligibility.
        h.ApplicantReader = new FakeEmployeeApplicantReader();
        var retriedBy = Guid.NewGuid();

        var result = await h.Handler().HandleAsync(h.Request(), retriedBy, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Empty(h.Service.AppointRequests);
        var resume = Assert.Single(h.Service.ResumeCalls);
        Assert.Equal(h.CompanyId, resume.CompanyId);
        Assert.Equal(h.Application.InternalAppointmentSourceReference, resume.SourceReference);
        Assert.Equal(retriedBy, resume.PerformedBy);
        Assert.Equal(0, h.ApplicantReader.Calls);

        // Completed as recorded — the new request's effective date/manager are NOT re-applied.
        Assert.Equal(recorded.PromotionId, result.Value!.PromotionId);
        Assert.Equal(recordedDate, result.Value.EffectiveDate);

        var saved = await h.ReloadApplicationAsync();
        Assert.Equal(InternalAppointmentStatus.Completed, saved.AppointmentStatus);
        Assert.Equal(recorded.PromotionId, saved.AppointmentPromotionId);
        Assert.Equal(recordedDate, saved.AppointmentEffectiveDate);
        Assert.Equal(h.Stages.Hired.Id, saved.CurrentStageId);
        Assert.Single(await h.Db.ApplicationStageHistoryEntries.ToListAsync());
        Assert.Single(h.Events.PublishedEvents.OfType<InternalCandidateAppointedIntegrationEvent>());
        Assert.Empty(h.Events.PublishedEvents.OfType<CandidateHiredIntegrationEvent>());
    }

    [Fact]
    public async Task HandleAsync_Pending_Without_Recorded_Change_Revalidates_And_Appoints()
    {
        var h = await SeedAsync(configure: (a, harness) =>
            a.BeginInternalAppointment(harness.EmployeeId, Guid.NewGuid(), Now.AddMinutes(-30)));

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(h.Service.ResumeCalls);
        Assert.Single(h.Service.AppointRequests);
        Assert.Equal(1, h.ApplicantReader.Calls);
        Assert.Equal(InternalAppointmentStatus.Completed, (await h.ReloadApplicationAsync()).AppointmentStatus);
    }

    [Fact]
    public async Task HandleAsync_Pending_Without_Recorded_Change_Still_Enforces_Eligibility()
    {
        var h = await SeedAsync(
            employmentState: EmployeeApplicantEmploymentState.Former,
            configure: (a, harness) => a.BeginInternalAppointment(harness.EmployeeId, Guid.NewGuid(), Now.AddMinutes(-30)));

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(h.Service.AppointRequests);
    }

    [Fact]
    public async Task HandleAsync_Recovery_Returns_Validation_When_No_Active_Hired_Stage()
    {
        var h = await SeedAsync(configure: (a, harness) =>
        {
            a.BeginInternalAppointment(harness.EmployeeId, Guid.NewGuid(), Now.AddMinutes(-30));
            harness.Stages.Hired.SetActiveStatus(false, Now);
        });
        h.Service.Seed(h.CompanyId, h.Application.InternalAppointmentSourceReference, h.EmployeeId, h.Vacancy.PositionProfileId, EffectiveDate);

        var result = await h.Handler().HandleAsync(h.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(h.Service.AppointRequests);

        // Left Pending so a later retry/reconciliation can complete it once a Hired stage exists.
        Assert.Equal(InternalAppointmentStatus.Pending, (await h.ReloadApplicationAsync()).AppointmentStatus);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>
    /// Asserts a refusal that happened before anything was recorded: the Employees module was never
    /// called, the application is not left Pending, its stage is unchanged and nothing was published.
    /// </summary>
    private static async Task AssertRefusedUntouchedAsync(
        Harness h, Result<AppointInternalCandidateResponse> result, string expectedCode)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Empty(h.Service.AppointRequests);
        Assert.Equal(0, h.Service.RecordedCount);

        var before = h.Application.CurrentStageId;
        var saved = await h.ReloadApplicationAsync();
        Assert.Null(saved.AppointmentStatus);
        Assert.Null(saved.AppointmentPromotionId);
        Assert.Equal(before, saved.CurrentStageId);

        Assert.Empty(await h.Db.ApplicationStageHistoryEntries.ToListAsync());
        Assert.Empty(h.Events.PublishedEvents);
        Assert.Empty(h.Audit.Published);
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
