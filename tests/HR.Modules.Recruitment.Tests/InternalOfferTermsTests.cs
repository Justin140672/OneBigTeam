using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.OfferCandidate;
using HR.Modules.Recruitment.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class InternalOfferTermsTests
{
    [Fact]
    public async Task Internal_Offer_Snapshots_The_Complete_Terms_On_The_Application()
    {
        var h = await InternalOfferHarness.CreateAsync();

        var saved = await h.OfferAsync();

        Assert.Equal(1, saved.OfferVersion);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, saved.OfferResponseStatus);
        Assert.Equal("Engineering Manager", saved.OfferJobTitle);
        Assert.Equal(h.PositionProfileId, saved.OfferPositionProfileId);
        Assert.Equal(h.DepartmentId, saved.OfferDepartmentId);
        Assert.Equal("Engineering", saved.OfferDepartmentName);
        Assert.Equal(h.LocationId, saved.OfferLocationId);
        Assert.Equal("London", saved.OfferLocationName);
        Assert.Equal(h.Vacancy.EmploymentTypeId, saved.OfferEmploymentTypeId);
        Assert.Equal(h.ManagerId, saved.OfferProposedManagerId);
        Assert.Equal("Mo Manager", saved.OfferProposedManagerName);
        Assert.False(saved.OfferNoManager);
        Assert.Equal("GBP", saved.OfferCurrency);
        Assert.Equal(72000m, saved.OfferedSalary);
        Assert.Equal(OfferSalaryFrequency.Annual, saved.OfferedSalaryFrequency);
        Assert.Equal(InternalOfferHarness.StartDate, saved.OfferedStartDate);
        Assert.Equal(WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday | WorkingDays.Friday, saved.OfferWorkingDays);
        Assert.Equal(7.5m, saved.OfferHoursPerDay);
        Assert.Equal(37.5m, saved.OfferHoursPerWeek);
        Assert.Equal(1m, saved.OfferFte);
        Assert.Equal(6, saved.OfferProbationMonths);
        Assert.Equal(new DateOnly(2026, 10, 20), saved.OfferResponseDeadline);
        Assert.Equal("Welcome to the team.", saved.OfferNotes);
        Assert.NotNull(saved.OfferTermsSnapshotAt);
        Assert.NotNull(saved.OfferMadeByUserId);
    }

    [Fact]
    public async Task Explicit_No_Manager_Is_Snapshotted_As_No_Manager()
    {
        var h = await InternalOfferHarness.CreateAsync();

        var saved = await h.OfferAsync(h.ValidOffer() with { ProposedManagerId = null, NoManager = true });

        Assert.True(saved.OfferNoManager);
        Assert.Null(saved.OfferProposedManagerId);
        Assert.True(saved.HasManagerDecision);
    }

    [Fact]
    public async Task Later_Position_Profile_Changes_Do_Not_Alter_The_Issued_Offer()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();

        var changedProfile = new FakePositionProfileReader(
            summaries: new Dictionary<Guid, PositionProfileSummary>
            {
                [h.PositionProfileId] = new(h.PositionProfileId, "Renamed", Guid.NewGuid(), true, Guid.NewGuid(), "Leeds", "Sales"),
            });
        var snapshot = await new HR.Modules.Recruitment.Services.OfferTermsSnapshotFactory(
                changedProfile, FakeEmploymentTypeReader.Permissive(), h.Wiring.Names)
            .BuildAsync(h.Vacancy, new HR.Modules.Recruitment.Services.OfferTermsInput(null, true, "GBP", null, null, null), CancellationToken.None);

        Assert.Equal("Sales", snapshot.DepartmentName);

        var saved = await h.ReloadAsync();
        Assert.Equal("Engineering", saved.OfferDepartmentName);
        Assert.Equal("London", saved.OfferLocationName);
    }

    [Fact]
    public async Task Internal_Offer_Creates_Exactly_One_Task_Effect_For_The_Linked_Employee()
    {
        var h = await InternalOfferHarness.CreateAsync();

        await h.OfferAsync();

        var effect = await h.Db.InternalOfferTaskEffects.SingleAsync();
        Assert.Equal(h.EmployeeId, effect.EmployeeId);
        Assert.Equal(h.Application.Id, effect.ApplicationId);
        Assert.Equal(1, effect.OfferVersion);
        Assert.Equal("Engineering Manager", effect.JobTitle);
        Assert.Equal(new DateOnly(2026, 10, 20), effect.ResponseDeadline);
        Assert.NotNull(effect.TaskCreatedAt);
        Assert.Single(h.Wiring.Creator.Created);
    }

    [Fact]
    public async Task External_Offer_Records_No_Snapshot_And_No_Task_Effect()
    {
        var h = await InternalOfferHarness.CreateAsync(internalApplication: false);

        var saved = await h.OfferAsync(h.ValidOffer() with
        {
            ProposedManagerId = null, Currency = null, ProposedStartDate = null, ResponseDeadline = null, Fte = null,
        });

        Assert.Null(saved.OfferTermsSnapshotAt);
        Assert.Empty(await h.Db.InternalOfferTaskEffects.ToListAsync());
        Assert.Empty(h.Wiring.Creator.Created);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("currency")]
    [InlineData("manager")]
    [InlineData("self-manager")]
    [InlineData("unknown-manager")]
    [InlineData("past-deadline")]
    public async Task Incomplete_Or_Invalid_Internal_Offer_Is_Rejected_And_Nothing_Is_Written(string problem)
    {
        var h = await InternalOfferHarness.CreateAsync();
        var request = problem switch
        {
            "start" => h.ValidOffer() with { ProposedStartDate = null },
            "currency" => h.ValidOffer() with { Currency = null },
            "manager" => h.ValidOffer() with { ProposedManagerId = null, NoManager = false },
            "self-manager" => h.ValidOffer() with { ProposedManagerId = h.EmployeeId },
            "unknown-manager" => h.ValidOffer() with { ProposedManagerId = Guid.NewGuid() },
            _ => h.ValidOffer() with { ResponseDeadline = new DateOnly(2026, 10, 1) },
        };

        var result = await h.OfferHandler().HandleAsync(request, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        var saved = await h.ReloadAsync();
        Assert.Null(saved.OfferResponseStatus);
        Assert.Equal(0, saved.OfferVersion);
        Assert.Empty(await h.Db.InternalOfferTaskEffects.ToListAsync());
    }

    [Fact]
    public async Task Offer_To_A_Non_Active_Employee_Is_Rejected()
    {
        var h = await InternalOfferHarness.CreateAsync();
        h.Wiring.Applicants.Set(FakeEmployeeApplicantReader.Profile(
            h.CompanyId, h.EmployeeId, state: EmployeeApplicantEmploymentState.Leaving));

        var result = await h.OfferHandler().HandleAsync(h.ValidOffer(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task Revised_Offer_Increments_The_Version_Resets_The_Response_And_Adds_A_New_Effect()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();
        var firstResponse = await h.RespondHandler().HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None);
        Assert.True(firstResponse.IsSuccess);

        var revised = await h.OfferAsync(h.ValidOffer() with { OfferedSalary = 75000m });

        Assert.Equal(2, revised.OfferVersion);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, revised.OfferResponseStatus);
        Assert.Null(revised.OfferRespondedAt);
        Assert.Null(revised.OfferRespondedByUserId);
        Assert.Equal(75000m, revised.OfferedSalary);
        Assert.Equal(2, (await h.Db.InternalOfferTaskEffects.ToListAsync()).Count);
    }

    [Fact]
    public async Task Revising_An_Offer_Cancels_The_Superseded_Task_And_Creates_A_New_One()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();

        await h.OfferAsync(h.ValidOffer() with { OfferedSalary = 75000m });

        Assert.Equal(2, h.Wiring.Creator.Created.Count);
        Assert.Contains(h.Wiring.Canceller.Calls, c => c.SourceEntityIds.Contains(h.Application.Id));
        var effects = await h.Db.InternalOfferTaskEffects.OrderBy(e => e.OfferVersion).ToListAsync();
        Assert.NotNull(effects[0].ClosedAt);
        Assert.Null(effects[1].ClosedAt);
        Assert.NotNull(effects[1].TaskCreatedAt);
    }

    [Fact]
    public async Task Offer_Cannot_Be_Revised_Once_The_Appointment_Has_Started()
    {
        var h = await InternalOfferHarness.CreateAsync();
        var saved = await h.OfferAsync();
        saved.RespondToOffer(OfferResponseStatus.Accepted, InternalOfferHarness.Now, h.EmployeeId, OfferResponseChannel.Employee, null);
        saved.BeginInternalAppointment(h.EmployeeId, Guid.NewGuid(), InternalOfferHarness.Now);
        await h.Db.SaveChangesAsync();

        var result = await h.OfferHandler().HandleAsync(h.ValidOffer(), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task Offer_Audit_Event_Carries_No_Salary()
    {
        var h = await InternalOfferHarness.CreateAsync();

        await h.OfferAsync();

        var audit = Assert.Single(h.Audit.Published.OfType<OfferDetailsRecordedAuditEvent>());
        var serialised = System.Text.Json.JsonSerializer.Serialize(
            new object?[] { ((HR.SharedKernel.IAuditEvent)audit).After, ((HR.SharedKernel.IAuditEvent)audit).Metadata });
        Assert.DoesNotContain("72000", serialised);
        Assert.DoesNotContain("salary", serialised, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Legacy_Offer_Snapshot_Backfill_Fills_Terms_And_Creates_The_Missing_Task_Effect()
    {
        var h = await InternalOfferHarness.CreateAsync(atOfferStage: true);
        h.Application.RecordOfferTerms(
            65000m, OfferSalaryFrequency.Annual, InternalOfferHarness.StartDate, new DateOnly(2026, 9, 20), null,
            InternalOfferHarness.Now.AddDays(-5), null, Guid.NewGuid());
        await h.Db.SaveChangesAsync();

        var backfill = new HR.Modules.Recruitment.Services.InternalOfferSnapshotBackfillService(
            h.Db, h.Wiring.Snapshot, h.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<HR.Modules.Recruitment.Services.InternalOfferSnapshotBackfillService>.Instance);

        var count = await backfill.BackfillAsync(CancellationToken.None);

        Assert.Equal(1, count);
        var saved = await h.ReloadAsync();
        Assert.NotNull(saved.OfferTermsSnapshotAt);
        Assert.Equal("Engineering Manager", saved.OfferJobTitle);
        Assert.Equal("Engineering", saved.OfferDepartmentName);
        Assert.Equal(65000m, saved.OfferedSalary);
        Assert.False(saved.HasManagerDecision);
        var effect = await h.Db.InternalOfferTaskEffects.SingleAsync();
        Assert.Equal(saved.OfferVersion, effect.OfferVersion);

        await h.Wiring.Effects.RunAllOutstandingAsync(CancellationToken.None);
        Assert.Single(h.Wiring.Creator.Created);
    }
}
