using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.BackfillEmployeeTimeline;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Employees.Tests;

public class BackfillEmployeeTimelineHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 7, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset Now = new(FixedUtcNow, TimeSpan.Zero);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private sealed record Harness(
        BackfillEmployeeTimelineHandler Handler,
        ScriptedReplayerBehavior ProbationBehavior,
        ScriptedReplayerBehavior OnboardingBehavior,
        ScriptedReplayerBehavior DocumentBehavior,
        ScriptedReplayerBehavior OffboardingBehavior);

    private static Harness BuildHandler(EmployeesDbContext dbContext)
    {
        var timelineWriter = new FakeEmployeeTimelineWriter();

        var probationBehavior = new ScriptedReplayerBehavior(dbContext, EmployeeTimelineEventType.ProbationPassed);
        var onboardingBehavior = new ScriptedReplayerBehavior(dbContext, EmployeeTimelineEventType.OnboardingCompleted);
        var documentBehavior = new ScriptedReplayerBehavior(dbContext, EmployeeTimelineEventType.CompanyDocumentAcknowledged);
        var offboardingBehavior = new ScriptedReplayerBehavior(dbContext, EmployeeTimelineEventType.OffboardingStarted);

        var handler = new BackfillEmployeeTimelineHandler(
            dbContext,
            timelineWriter,
            new FakeProbationHistoryReplayer(probationBehavior),
            new FakeOnboardingHistoryReplayer(onboardingBehavior),
            new FakeSharedCompanyDocumentAcknowledgementHistoryReplayer(documentBehavior),
            new FakeOffboardingHistoryReplayer(offboardingBehavior),
            new FakeClock(FixedUtcNow),
            NullLogger<BackfillEmployeeTimelineHandler>.Instance);

        return new Harness(handler, probationBehavior, onboardingBehavior, documentBehavior, offboardingBehavior);
    }

    private static Employee AddEmployee(EmployeesDbContext dbContext, Guid companyId)
    {
        var employee = Employee.Create(
            Guid.NewGuid(),
            companyId,
            "Jamie",
            "Smith",
            $"jamie.smith.{Guid.NewGuid():N}@example.com",
            new DateOnly(2020, 1, 1),
            hasSystemAccess: true,
            new DateOnly(1990, 1, 1),
            "British",
            "Female",
            $"EMP-{Guid.NewGuid():N}"[..12],
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Now.AddYears(-1));
        dbContext.Employees.Add(employee);
        return employee;
    }

    private static EmployeePromotion AddCompletedPromotion(EmployeesDbContext dbContext, Guid companyId, Guid employeeId)
    {
        var promotion = EmployeePromotion.Create(
            Guid.NewGuid(),
            companyId,
            employeeId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            newManagerId: null,
            newLocationId: null,
            new DateOnly(2026, 1, 1),
            "Promotion",
            notes: null,
            compensationId: null,
            Guid.NewGuid(),
            Now.AddMonths(-6));
        promotion.Complete(Now.AddMonths(-5));
        dbContext.EmployeePromotions.Add(promotion);
        return promotion;
    }

    private static Compensation AddCompensation(EmployeesDbContext dbContext, Guid companyId, Guid employeeId)
    {
        var compensation = Compensation.Create(
            Guid.NewGuid(),
            companyId,
            employeeId,
            new DateOnly(2026, 1, 1),
            SalaryType.Annual,
            50000m,
            "GBP",
            hoursPerWeek: null,
            fte: null,
            notes: null,
            CompensationChangeReason.AnnualReview,
            Guid.NewGuid(),
            Now.AddMonths(-6));
        dbContext.Compensations.Add(compensation);
        return compensation;
    }

    [Fact]
    public async Task HandleAsync_Reports_Correct_Totals_Across_All_Seven_Sources()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = AddEmployee(dbContext, companyId);
        AddCompletedPromotion(dbContext, companyId, employee.Id);
        AddCompensation(dbContext, companyId, employee.Id);
        await dbContext.SaveChangesAsync();

        var harness = BuildHandler(dbContext);
        harness.ProbationBehavior.EntriesToCreate = 2;
        harness.OnboardingBehavior.EntriesToCreate = 1;
        harness.DocumentBehavior.EntriesToCreate = 3;
        harness.OffboardingBehavior.EntriesToCreate = 1;

        var result = await harness.Handler.HandleAsync(
            new BackfillEmployeeTimelineRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value!;

        Assert.Equal(companyId, response.CompanyId);
        Assert.Equal(7, response.Sources.Count);
        Assert.Equal(0, response.TotalFailed);

        // 1 EmployeeJoined + 1 EmployeePromoted + 1 CompensationChanged
        // + 2 ProbationPassed + 1 OnboardingCompleted + 3 SharedCompanyDocumentAcknowledged + 1 OffboardingStarted
        Assert.Equal(10, response.TotalCreated);
        Assert.Equal(0, response.TotalSkipped);

        var offboardingResult = Assert.Single(response.Sources, s => s.Source == "OffboardingStarted");
        Assert.Equal(1, offboardingResult.Created);
        Assert.Equal(0, offboardingResult.Skipped);
        Assert.Equal(0, offboardingResult.Failed);

        var probationResult = Assert.Single(response.Sources, s => s.Source == "ProbationPassed");
        Assert.Equal(2, probationResult.Created);

        var employeeCreatedResult = Assert.Single(response.Sources, s => s.Source == "EmployeeCreated");
        Assert.Equal(1, employeeCreatedResult.Created);

        var employeePromotedResult = Assert.Single(response.Sources, s => s.Source == "EmployeePromoted");
        Assert.Equal(1, employeePromotedResult.Created);

        var compensationResult = Assert.Single(response.Sources, s => s.Source == "CompensationChanged");
        Assert.Equal(1, compensationResult.Created);
    }

    [Fact]
    public async Task HandleAsync_CrossModuleSource_Reports_Skipped_When_Processed_Exceeds_Created()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var harness = BuildHandler(dbContext);
        // Simulate 3 historical offboarding plans processed, but only 1 new timeline entry actually
        // created (the other 2 already had entries from a prior backfill run) — no self-dedup lives
        // in the replayer, so this can only be represented via ProcessedOverride here.
        harness.OffboardingBehavior.EntriesToCreate = 1;
        harness.OffboardingBehavior.ProcessedOverride = 3;

        var result = await harness.Handler.HandleAsync(
            new BackfillEmployeeTimelineRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var offboardingResult = Assert.Single(result.Value!.Sources, s => s.Source == "OffboardingStarted");
        Assert.Equal(1, offboardingResult.Created);
        Assert.Equal(2, offboardingResult.Skipped);
        Assert.Equal(0, offboardingResult.Failed);
    }

    [Fact]
    public async Task HandleAsync_Isolates_Failure_Of_OffboardingHistoryReplayer_From_Other_Sources()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var employee = AddEmployee(dbContext, companyId);
        AddCompletedPromotion(dbContext, companyId, employee.Id);
        AddCompensation(dbContext, companyId, employee.Id);
        await dbContext.SaveChangesAsync();

        var harness = BuildHandler(dbContext);
        harness.ProbationBehavior.EntriesToCreate = 1;
        harness.OnboardingBehavior.EntriesToCreate = 1;
        harness.DocumentBehavior.EntriesToCreate = 1;
        harness.OffboardingBehavior.ShouldThrow = true;

        var result = await harness.Handler.HandleAsync(
            new BackfillEmployeeTimelineRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        Assert.Equal(7, response.Sources.Count);

        var offboardingResult = Assert.Single(response.Sources, s => s.Source == "OffboardingStarted");
        Assert.Equal(0, offboardingResult.Created);
        Assert.Equal(0, offboardingResult.Skipped);
        Assert.Equal(1, offboardingResult.Failed);
        Assert.Equal(1, response.TotalFailed);

        // The 6 other sources still ran successfully: EmployeeCreated, EmployeePromoted,
        // CompensationChanged, ProbationPassed, OnboardingCompleted, SharedCompanyDocumentAcknowledged.
        var otherResults = response.Sources.Where(s => s.Source != "OffboardingStarted").ToList();
        Assert.Equal(6, otherResults.Count);
        Assert.All(otherResults, s => Assert.Equal(0, s.Failed));
        Assert.Equal(1, Assert.Single(otherResults, s => s.Source == "EmployeeCreated").Created);
        Assert.Equal(1, Assert.Single(otherResults, s => s.Source == "EmployeePromoted").Created);
        Assert.Equal(1, Assert.Single(otherResults, s => s.Source == "CompensationChanged").Created);
        Assert.Equal(1, Assert.Single(otherResults, s => s.Source == "ProbationPassed").Created);
        Assert.Equal(1, Assert.Single(otherResults, s => s.Source == "OnboardingCompleted").Created);
        Assert.Equal(1, Assert.Single(otherResults, s => s.Source == "SharedCompanyDocumentAcknowledged").Created);
    }

    [Fact]
    public async Task HandleAsync_Returns_Zeroes_For_Company_With_No_Historical_Records()
    {
        await using var dbContext = BuildContext();
        var companyId = Guid.NewGuid();

        var harness = BuildHandler(dbContext);

        var result = await harness.Handler.HandleAsync(
            new BackfillEmployeeTimelineRequest(companyId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value!;
        Assert.Equal(0, response.TotalCreated);
        Assert.Equal(0, response.TotalSkipped);
        Assert.Equal(0, response.TotalFailed);
        Assert.All(response.Sources, s =>
        {
            Assert.Equal(0, s.Created);
            Assert.Equal(0, s.Skipped);
            Assert.Equal(0, s.Failed);
        });
    }
}
