using HR.SharedKernel;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.AppointInternalCandidate;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Recruitment.Tests;

public class AppointInternalCandidateAcceptedTermsTests
{
    private sealed class Setup
    {
        public required InternalOfferHarness Offer { get; init; }
        public required FakeEmployeeInternalAppointmentService Service { get; init; }
        public required FakeIntegrationEventPublisher Events { get; init; }
        public required FakeAuditPublisher Audit { get; init; }

        public AppointInternalCandidateHandler Handler()
        {
            var clock = Offer.Clock;
            var recorder = new RecruitmentStageChangeRecorder(Offer.Db, Events, Audit);
            var completer = new InternalAppointmentCompleter(Offer.Db, clock, Events, Audit, recorder);
            return new AppointInternalCandidateHandler(
                Offer.Db, Offer.Wiring.Applicants, Offer.Wiring.Positions, Service, completer, clock,
                NullLogger<AppointInternalCandidateHandler>.Instance);
        }

        public AppointInternalCandidateRequest Request() => new()
        {
            CompanyId = Offer.CompanyId,
            VacancyId = Offer.Vacancy.Id,
            ApplicationId = Offer.Application.Id,
        };
    }

    private static async Task<Setup> SetupAsync(Func<InternalOfferHarness, Task>? beforeRespond = null, bool accept = true)
    {
        var offer = await InternalOfferHarness.CreateAsync();
        await offer.OfferAsync();
        if (beforeRespond is not null)
            await beforeRespond(offer);

        if (accept)
        {
            var response = await offer.RespondHandler().HandleAsync(offer.Respond("Accept"), offer.EmployeeId, CancellationToken.None);
            Assert.True(response.IsSuccess, response.IsFailure ? response.Error.Message : null);
        }

        return new Setup
        {
            Offer = offer,
            Service = new FakeEmployeeInternalAppointmentService(),
            Events = new FakeIntegrationEventPublisher(),
            Audit = new FakeAuditPublisher(),
        };
    }

    [Fact]
    public async Task Appointment_Defaults_To_The_Terms_The_Employee_Accepted()
    {
        var s = await SetupAsync();

        var result = await s.Handler().HandleAsync(s.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var request = Assert.Single(s.Service.AppointRequests);
        Assert.Equal(InternalOfferHarness.StartDate, request.EffectiveDate);
        Assert.Equal(s.Offer.ManagerId, request.NewManagerId);
        Assert.Equal(s.Offer.PositionProfileId, request.NewPositionProfileId);
        Assert.NotNull(request.Compensation);
        Assert.Equal("Annual", request.Compensation!.SalaryType);
        Assert.Equal(72000m, request.Compensation.Salary);
        Assert.Equal("GBP", request.Compensation.Currency);
        Assert.Equal(37.5m, request.Compensation.HoursPerWeek);
        Assert.Equal(1m, request.Compensation.Fte);
        Assert.Contains("Engineering Manager", request.Reason);
    }

    [Fact]
    public async Task Accepted_No_Manager_Is_Honoured_Without_The_Request_Repeating_It()
    {
        var offer = await InternalOfferHarness.CreateAsync();
        await offer.OfferAsync(offer.ValidOffer() with { ProposedManagerId = null, NoManager = true });
        await offer.RespondHandler().HandleAsync(offer.Respond("Accept"), offer.EmployeeId, CancellationToken.None);
        var s = new Setup
        {
            Offer = offer, Service = new FakeEmployeeInternalAppointmentService(),
            Events = new FakeIntegrationEventPublisher(), Audit = new FakeAuditPublisher(),
        };

        var result = await s.Handler().HandleAsync(s.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(s.Service.AppointRequests).NewManagerId);
    }

    [Fact]
    public async Task Appointing_With_The_Same_Terms_Explicitly_Succeeds()
    {
        var s = await SetupAsync();

        var result = await s.Handler().HandleAsync(
            s.Request() with
            {
                EffectiveDate = InternalOfferHarness.StartDate,
                ManagerId = s.Offer.ManagerId,
                CreateCompensationChange = true,
                CompensationSalaryType = "Annual",
                CompensationSalary = 72000m,
                CompensationCurrency = "GBP",
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Theory]
    [InlineData("date")]
    [InlineData("manager")]
    [InlineData("no-manager")]
    [InlineData("salary")]
    [InlineData("currency")]
    [InlineData("frequency")]
    public async Task Changing_A_Material_Term_After_Acceptance_Requires_A_Revised_Offer(string change)
    {
        var s = await SetupAsync();
        var request = change switch
        {
            "date" => s.Request() with { EffectiveDate = InternalOfferHarness.StartDate.AddDays(7) },
            "manager" => s.Request() with { ManagerId = Guid.NewGuid() },
            "no-manager" => s.Request() with { NoManager = true },
            "salary" => s.Request() with
            {
                CreateCompensationChange = true, CompensationSalaryType = "Annual", CompensationSalary = 90000m, CompensationCurrency = "GBP",
            },
            "currency" => s.Request() with
            {
                CreateCompensationChange = true, CompensationSalaryType = "Annual", CompensationSalary = 72000m, CompensationCurrency = "EUR",
            },
            _ => s.Request() with
            {
                CreateCompensationChange = true, CompensationSalaryType = "Hourly", CompensationSalary = 72000m, CompensationCurrency = "GBP",
            },
        };

        var result = await s.Handler().HandleAsync(request, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("revised offer", result.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(s.Service.AppointRequests);
        Assert.Null((await s.Offer.ReloadAsync()).AppointmentStatus);
    }

    [Fact]
    public async Task A_Revised_Offer_Must_Be_Accepted_Again_Before_Appointing()
    {
        var s = await SetupAsync();
        await s.Offer.OfferAsync(s.Offer.ValidOffer() with { OfferedSalary = 80000m });

        var blocked = await s.Handler().HandleAsync(s.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(blocked.IsFailure);
        Assert.Contains("awaiting", blocked.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(s.Service.AppointRequests);

        var accept = await s.Offer.RespondHandler().HandleAsync(s.Offer.Respond("Accept", version: 2), s.Offer.EmployeeId, CancellationToken.None);
        Assert.True(accept.IsSuccess);

        var appointed = await s.Handler().HandleAsync(s.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(appointed.IsSuccess, appointed.IsFailure ? appointed.Error.Message : null);
        Assert.Equal(80000m, Assert.Single(s.Service.AppointRequests).Compensation!.Salary);
    }

    [Theory]
    [InlineData("awaiting")]
    [InlineData("declined")]
    [InlineData("withdrawn")]
    public async Task Appointment_Is_Blocked_Unless_The_Offer_Was_Accepted(string state)
    {
        var s = await SetupAsync(accept: false);
        if (state == "declined")
            await s.Offer.RespondHandler().HandleAsync(s.Offer.Respond("Decline"), s.Offer.EmployeeId, CancellationToken.None);
        else if (state == "withdrawn")
        {
            var application = await s.Offer.ReloadAsync();
            application.RespondToOffer(OfferResponseStatus.Withdrawn, InternalOfferHarness.Now);
            await s.Offer.Db.SaveChangesAsync();
        }

        var result = await s.Handler().HandleAsync(s.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(s.Service.AppointRequests);
    }

    [Fact]
    public async Task Appointment_Uses_The_Offered_Position_Profile_Even_If_The_Vacancy_Profile_Changes_Later()
    {
        var s = await SetupAsync();
        var newProfileId = Guid.NewGuid();
        var changed = new FakePositionProfileReader(summaries: new Dictionary<Guid, PositionProfileSummary>
        {
            [s.Offer.PositionProfileId] = new(s.Offer.PositionProfileId, "Engineering Manager", s.Offer.DepartmentId, true, s.Offer.LocationId, "London"),
            [newProfileId] = new(newProfileId, "Other", Guid.NewGuid(), true, Guid.NewGuid(), "Leeds"),
        });
        var clock = s.Offer.Clock;
        var recorder = new RecruitmentStageChangeRecorder(s.Offer.Db, s.Events, s.Audit);
        var handler = new AppointInternalCandidateHandler(
            s.Offer.Db, s.Offer.Wiring.Applicants, changed, s.Service,
            new InternalAppointmentCompleter(s.Offer.Db, clock, s.Events, s.Audit, recorder), clock,
            NullLogger<AppointInternalCandidateHandler>.Instance);

        var result = await handler.HandleAsync(s.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(s.Offer.PositionProfileId, Assert.Single(s.Service.AppointRequests).NewPositionProfileId);
    }

    [Fact]
    public async Task Future_Dated_Accepted_Offer_Is_Appointed_As_Scheduled_And_Employee_Identity_Is_Untouched()
    {
        var s = await SetupAsync();
        s.Service.Today = new DateOnly(2026, 10, 6);

        var result = await s.Handler().HandleAsync(s.Request(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.False(result.Value!.IsApplied);
        Assert.Equal(InternalOfferHarness.StartDate, result.Value.EffectiveDate);
        Assert.Equal(s.Offer.EmployeeId, result.Value.EmployeeId);
        Assert.Empty(s.Events.PublishedEvents.OfType<CandidateHiredIntegrationEvent>());
        var saved = await s.Offer.ReloadAsync();
        Assert.Equal(InternalAppointmentStatus.Completed, saved.AppointmentStatus);
    }

    [Fact]
    public async Task Accepting_The_Offer_Does_Not_Create_An_Employee_Onboarding_Or_Hire_Event()
    {
        var offer = await InternalOfferHarness.CreateAsync();
        await offer.OfferAsync();

        await offer.RespondHandler().HandleAsync(offer.Respond("Accept"), offer.EmployeeId, CancellationToken.None);

        Assert.Empty(offer.Events.PublishedEvents.OfType<CandidateHiredIntegrationEvent>());
        var saved = await offer.ReloadAsync();
        Assert.Null(saved.AppointmentStatus);
        Assert.Equal(offer.Stages.Offer.Id, saved.CurrentStageId);
        Assert.Equal(offer.EmployeeId, (await offer.Db.Candidates.SingleAsync()).EmployeeId);
    }

    [Fact]
    public async Task Legacy_Accepted_Offer_Without_A_Snapshot_Still_Uses_The_Request_For_Manager_And_Date()
    {
        var offer = await InternalOfferHarness.CreateAsync(atOfferStage: true);
        offer.Application.RecordOfferTerms(65000m, OfferSalaryFrequency.Annual, null, new DateOnly(2026, 9, 20), null, InternalOfferHarness.Now.AddDays(-5));
        offer.Application.RespondToOffer(OfferResponseStatus.Accepted, InternalOfferHarness.Now.AddDays(-4));
        await offer.Db.SaveChangesAsync();
        var s = new Setup
        {
            Offer = offer, Service = new FakeEmployeeInternalAppointmentService(),
            Events = new FakeIntegrationEventPublisher(), Audit = new FakeAuditPublisher(),
        };

        var missing = await s.Handler().HandleAsync(s.Request(), Guid.NewGuid(), CancellationToken.None);
        Assert.True(missing.IsFailure);

        var result = await s.Handler().HandleAsync(
            s.Request() with { EffectiveDate = InternalOfferHarness.StartDate, ManagerId = offer.ManagerId },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Null(Assert.Single(s.Service.AppointRequests).Compensation);
    }
}
