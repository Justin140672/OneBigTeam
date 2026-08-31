using HR.Modules.Employees.Contracts;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.GetManagerTeamStatusSummary;
using HR.Modules.Employees.Persistence;
using HR.Modules.Employees.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Employees.Tests;

/// <summary>
/// DSH-05 manager team-status summary handler. "Today" is a Monday (2026-06-15) so the default
/// Mon–Fri working pattern counts as a working day unless a test overrides it.
/// </summary>
public class GetManagerTeamStatusSummaryHandlerTests
{
    private static readonly DateTime MondayUtcNow = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 6, 15);
    private static readonly DateOnly StartPast = new(2026, 1, 1);
    private static readonly DateTimeOffset Now = new(MondayUtcNow, TimeSpan.Zero);

    private static EmployeesDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<EmployeesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static GetManagerTeamStatusSummaryHandler BuildHandler(
        EmployeesDbContext context,
        Guid[] subtree,
        FakeEmployeeLeaveStatusReader? leave = null,
        FakeEmployeesOffSickReader? sick = null,
        FakeEmployeesInProbationReader? probation = null,
        FakeEmployeesMissingFitNoteReader? fitNotes = null,
        string timeZoneId = "UTC",
        DateTime? utcNow = null) =>
        new(context,
            new FakeDirectReportsReader(subtree),
            new FakeCompanyLeaveSettingsReader(),
            leave ?? new FakeEmployeeLeaveStatusReader(),
            sick ?? new FakeEmployeesOffSickReader(),
            probation ?? new FakeEmployeesInProbationReader(),
            fitNotes ?? new FakeEmployeesMissingFitNoteReader(),
            new FakeClock(utcNow ?? MondayUtcNow),
            new FakeCompanyTimeZoneReader(timeZoneId));

    private static Employee AddEmployee(
        EmployeesDbContext context,
        Guid companyId,
        string first = "Test",
        string last = "Employee",
        DateOnly? startDate = null,
        DateOnly? leavingDate = null,
        EmploymentStatus status = EmploymentStatus.Active,
        Guid? managerId = null,
        Guid? positionProfileId = null,
        WorkingDays? workingDaysOverride = null,
        decimal? hoursPerDayOverride = null)
    {
        var employee = Employee.Create(
            Guid.NewGuid(), companyId, first, last, $"{first}.{last}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
            startDate ?? StartPast, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            $"EMP-{Guid.NewGuid():N}", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), positionProfileId ?? Guid.NewGuid(), Now);

        if (managerId is not null || positionProfileId is not null)
            employee.Assign(Guid.NewGuid(), positionProfileId ?? employee.PositionProfileId, Guid.NewGuid(), managerId, Now);

        switch (status)
        {
            case EmploymentStatus.Draft:
                break;
            case EmploymentStatus.Active:
                employee.Activate(Now);
                break;
            default:
                employee.SetStatusForTesting(status, Now);
                break;
        }

        if (leavingDate is not null)
            employee.UpdateEmploymentDetails(
                employee.EmployeeNumber, employee.EmploymentTypeId, employee.StartDate,
                null, null, leavingDate, null, Now);

        if (workingDaysOverride is not null || hoursPerDayOverride is not null)
            employee.SetWorkingPattern(workingDaysOverride, hoursPerDayOverride, Now);

        context.Employees.Add(employee);
        return employee;
    }

    // ── counted population ───────────────────────────────────────────────────

    [Fact]
    public async Task TeamSize_Excludes_NonActive_NotYetStarted_And_Already_Left_But_Keeps_Inclusive_Boundaries()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var active = AddEmployee(context, companyId, "Anna", "Active");
        var startsToday = AddEmployee(context, companyId, "Sam", "StartsToday", startDate: Today);
        var leavesToday = AddEmployee(context, companyId, "Lee", "LeavesToday", leavingDate: Today);
        var draft = AddEmployee(context, companyId, "Dan", "Draft", status: EmploymentStatus.Draft);
        var suspended = AddEmployee(context, companyId, "Sue", "Suspended", status: EmploymentStatus.Suspended);
        var leaving = AddEmployee(context, companyId, "Liz", "Leaving", status: EmploymentStatus.Leaving);
        var former = AddEmployee(context, companyId, "Fred", "Former", status: EmploymentStatus.FormerEmployee);
        var future = AddEmployee(context, companyId, "Fay", "Future", startDate: Today.AddDays(1));
        var alreadyLeft = AddEmployee(context, companyId, "Otto", "Gone", leavingDate: Today.AddDays(-1));
        await context.SaveChangesAsync();

        var subtree = new[]
        {
            active.Id, startsToday.Id, leavesToday.Id, draft.Id, suspended.Id,
            leaving.Id, former.Id, future.Id, alreadyLeft.Id,
        };
        var handler = BuildHandler(context, subtree);

        var result = await handler.HandleAsync(companyId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(3, result.TeamSize);
        Assert.Equal(3, result.Members.Count);
        Assert.Equal(
            new[] { active.Id, leavesToday.Id, startsToday.Id }.OrderBy(x => x),
            result.Members.Select(m => m.EmployeeId).OrderBy(x => x));
    }

    [Fact]
    public async Task Empty_Subtree_Returns_All_Zeros_And_No_Members()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context, []);

        var result = await handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, result.TeamSize);
        Assert.Equal(0, result.AtWork);
        Assert.Equal(0, result.AwayToday);
        Assert.Equal(0, result.OnLeave);
        Assert.Equal(0, result.Sick);
        Assert.Equal(0, result.InProbation);
        Assert.Equal(0, result.MissingFitNotes);
        Assert.Equal(0, result.NotScheduledToday);
        Assert.Empty(result.Members);
    }

    [Fact]
    public async Task Subtree_With_No_Counted_Members_Returns_All_Zeros()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var draft = AddEmployee(context, companyId, status: EmploymentStatus.Draft);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, [draft.Id]);
        var result = await handler.HandleAsync(companyId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0, result.TeamSize);
        Assert.Empty(result.Members);
    }

    // ── at-work vs not-scheduled ─────────────────────────────────────────────

    [Fact]
    public async Task AtWork_Excludes_A_Counted_Member_Not_Scheduled_To_Work_Today()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var scheduled = AddEmployee(context, companyId, "Will", "Working");
        var offToday = AddEmployee(context, companyId, "Tara", "TuesdayOnly",
            workingDaysOverride: WorkingDays.Tuesday, hoursPerDayOverride: 7.5m);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, [scheduled.Id, offToday.Id]);
        var result = await handler.HandleAsync(companyId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, result.TeamSize);
        Assert.Equal(1, result.AtWork);
        Assert.Equal(1, result.NotScheduledToday);
        Assert.Equal(0, result.AwayToday);
        Assert.Equal(result.TeamSize, result.AtWork + result.AwayToday + result.NotScheduledToday);

        Assert.Equal("AtWork", result.Members.Single(m => m.EmployeeId == scheduled.Id).PrimaryStatus);
        var off = result.Members.Single(m => m.EmployeeId == offToday.Id);
        Assert.False(off.ScheduledToday);
        Assert.Equal("NotScheduled", off.PrimaryStatus);
    }

    [Fact]
    public async Task PositionProfile_Working_Pattern_Override_Applies_When_Employee_Has_None()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var profileId = Guid.NewGuid();
        context.PositionProfiles.Add(PositionProfile.Create(
            profileId, companyId, Guid.NewGuid(), Guid.NewGuid(), "Part timer", null, null,
            WorkingDays.Tuesday | WorkingDays.Wednesday, 7.5m, null, null, null, Guid.NewGuid(), Now));
        var member = AddEmployee(context, companyId, "Pat", "Profiled", positionProfileId: profileId);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, [member.Id]);
        var result = await handler.HandleAsync(companyId, Guid.NewGuid(), CancellationToken.None);

        var item = Assert.Single(result.Members);
        Assert.False(item.ScheduledToday); // Monday is not in the profile's Tue/Wed pattern
        Assert.Equal("NotScheduled", item.PrimaryStatus);
    }

    // ── absence precedence ───────────────────────────────────────────────────

    [Fact]
    public async Task Member_On_Leave_Only_Has_PrimaryStatus_OnLeave()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var member = AddEmployee(context, companyId);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, [member.Id],
            leave: new FakeEmployeeLeaveStatusReader(member.Id));
        var result = await handler.HandleAsync(companyId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(1, result.OnLeave);
        Assert.Equal(0, result.Sick);
        Assert.Equal(1, result.AwayToday);
        Assert.Equal(0, result.AtWork);
        Assert.Equal("OnLeave", Assert.Single(result.Members).PrimaryStatus);
    }

    [Fact]
    public async Task Member_Both_On_Leave_And_Sick_Counts_Once_In_AwayToday_And_Ranks_As_Sick()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var member = AddEmployee(context, companyId);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, [member.Id],
            leave: new FakeEmployeeLeaveStatusReader(member.Id),
            sick: new FakeEmployeesOffSickReader(member.Id));
        var result = await handler.HandleAsync(companyId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(1, result.OnLeave);
        Assert.Equal(1, result.Sick);
        Assert.Equal(1, result.AwayToday); // overlap counted once
        Assert.Equal(0, result.AtWork);

        var item = Assert.Single(result.Members);
        Assert.True(item.OnLeaveToday);
        Assert.True(item.OffSickToday);
        Assert.Equal("Sick", item.PrimaryStatus);
    }

    // ── probation / fit notes ────────────────────────────────────────────────

    [Fact]
    public async Task InProbation_And_MissingFitNotes_Come_From_Their_Readers_With_Drilldown_Parity()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var probationer = AddEmployee(context, companyId, "Prue", "Probation");
        var noNote = AddEmployee(context, companyId, "Nora", "NoNote");
        var plain = AddEmployee(context, companyId, "Percy", "Plain");
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, [probationer.Id, noNote.Id, plain.Id],
            probation: new FakeEmployeesInProbationReader(probationer.Id),
            fitNotes: new FakeEmployeesMissingFitNoteReader(noNote.Id));
        var result = await handler.HandleAsync(companyId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(1, result.InProbation);
        Assert.Equal(1, result.MissingFitNotes);
        Assert.Equal(result.InProbation, result.Members.Count(m => m.InProbation));
        Assert.Equal(result.MissingFitNotes, result.Members.Count(m => m.MissingFitNote));
        Assert.True(result.Members.Single(m => m.EmployeeId == probationer.Id).InProbation);
        Assert.True(result.Members.Single(m => m.EmployeeId == noNote.Id).MissingFitNote);
        // A member with only an "upcoming review" (not returned by the reader) is not counted.
        Assert.False(result.Members.Single(m => m.EmployeeId == plain.Id).InProbation);
    }

    // ── drill-down parity across every metric ────────────────────────────────

    [Fact]
    public async Task Every_Count_Equals_Members_Filtered_By_The_Matching_Flag()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var atWork = AddEmployee(context, companyId, "A", "AtWork");
        var onLeave = AddEmployee(context, companyId, "B", "OnLeave");
        var sick = AddEmployee(context, companyId, "C", "Sick");
        var both = AddEmployee(context, companyId, "D", "Both");
        var notScheduled = AddEmployee(context, companyId, "E", "Off",
            workingDaysOverride: WorkingDays.Sunday, hoursPerDayOverride: 7.5m);
        var probationer = AddEmployee(context, companyId, "F", "Probation");
        var missingNote = AddEmployee(context, companyId, "G", "MissingNote");
        await context.SaveChangesAsync();

        var subtree = new[]
        {
            atWork.Id, onLeave.Id, sick.Id, both.Id, notScheduled.Id, probationer.Id, missingNote.Id,
        };
        var handler = BuildHandler(context, subtree,
            leave: new FakeEmployeeLeaveStatusReader(onLeave.Id, both.Id),
            sick: new FakeEmployeesOffSickReader(sick.Id, both.Id),
            probation: new FakeEmployeesInProbationReader(probationer.Id),
            fitNotes: new FakeEmployeesMissingFitNoteReader(missingNote.Id));

        var result = await handler.HandleAsync(companyId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(result.TeamSize, result.Members.Count);
        Assert.Equal(result.OnLeave, result.Members.Count(m => m.OnLeaveToday));
        Assert.Equal(result.Sick, result.Members.Count(m => m.OffSickToday));
        Assert.Equal(result.InProbation, result.Members.Count(m => m.InProbation));
        Assert.Equal(result.MissingFitNotes, result.Members.Count(m => m.MissingFitNote));
        Assert.Equal(result.AtWork, result.Members.Count(m => m.PrimaryStatus == "AtWork"));
        Assert.Equal(result.NotScheduledToday, result.Members.Count(m => m.PrimaryStatus == "NotScheduled"));
        Assert.Equal(result.AwayToday, result.Members.Count(m => m.OnLeaveToday || m.OffSickToday));
        Assert.Equal(3, result.AwayToday); // onLeave + sick + both
    }

    [Fact]
    public async Task Members_Are_Ordered_By_LastName_Then_FirstName()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var young = AddEmployee(context, companyId, "Zoe", "Young");
        var adamsB = AddEmployee(context, companyId, "Bob", "Adams");
        var adamsA = AddEmployee(context, companyId, "Ann", "Adams");
        await context.SaveChangesAsync();

        var handler = BuildHandler(context, [young.Id, adamsB.Id, adamsA.Id]);
        var result = await handler.HandleAsync(companyId, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(new[] { adamsA.Id, adamsB.Id, young.Id }, result.Members.Select(m => m.EmployeeId));
    }

    // ── readers only see the counted population ──────────────────────────────

    [Fact]
    public async Task Readers_Are_Passed_Only_The_Counted_Member_Ids_And_Todays_Date()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var counted = AddEmployee(context, companyId, "In", "Scope");
        var draft = AddEmployee(context, companyId, "Out", "Scope", status: EmploymentStatus.Draft);
        await context.SaveChangesAsync();

        var leave = new FakeEmployeeLeaveStatusReader();
        var sick = new FakeEmployeesOffSickReader();
        var probation = new FakeEmployeesInProbationReader();
        var fitNotes = new FakeEmployeesMissingFitNoteReader();

        var handler = BuildHandler(context, [counted.Id, draft.Id], leave, sick, probation, fitNotes);
        await handler.HandleAsync(companyId, Guid.NewGuid(), CancellationToken.None);

        foreach (var requested in new[]
                 {
                     leave.LastRequestedIds, sick.LastRequestedIds,
                     probation.LastRequestedIds, fitNotes.LastRequestedIds,
                 })
        {
            Assert.NotNull(requested);
            Assert.Equal(new[] { counted.Id }, requested);
        }

        Assert.Equal(Today, sick.LastOnDate);
    }

    // ── company time zone drives "today" ────────────────────────────────────

    [Fact]
    public async Task Company_TimeZone_Ahead_Of_UTC_Rolls_Today_Forward_To_A_Working_Day()
    {
        // 2026-06-14 23:30Z is still Sunday in UTC but already Monday 00:30 in Europe/London (BST).
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var member = AddEmployee(context, companyId); // default Mon–Fri pattern
        await context.SaveChangesAsync();

        var utcNow = new DateTime(2026, 6, 14, 23, 30, 0, DateTimeKind.Utc);

        var london = BuildHandler(context, [member.Id], timeZoneId: "Europe/London", utcNow: utcNow);
        var londonResult = await london.HandleAsync(companyId, Guid.NewGuid(), CancellationToken.None);
        Assert.True(londonResult.Members.Single().ScheduledToday);
        Assert.Equal("AtWork", londonResult.Members.Single().PrimaryStatus);

        var utc = BuildHandler(context, [member.Id], timeZoneId: "UTC", utcNow: utcNow);
        var utcResult = await utc.HandleAsync(companyId, Guid.NewGuid(), CancellationToken.None);
        Assert.False(utcResult.Members.Single().ScheduledToday); // Sunday in UTC
        Assert.Equal("NotScheduled", utcResult.Members.Single().PrimaryStatus);
    }

    [Fact]
    public async Task Counted_Population_Is_Isolated_By_Company()
    {
        await using var context = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var a = AddEmployee(context, companyA, "A", "One");
        var b = AddEmployee(context, companyB, "B", "Two");
        await context.SaveChangesAsync();

        // Even though the (fake) sub-tree reader hands back both ids, the handler's own query is
        // company scoped.
        var handler = BuildHandler(context, [a.Id, b.Id]);
        var result = await handler.HandleAsync(companyA, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(1, result.TeamSize);
        Assert.Equal(a.Id, Assert.Single(result.Members).EmployeeId);
    }
}
