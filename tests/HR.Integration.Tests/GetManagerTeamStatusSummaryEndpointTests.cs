using System.Net;
using System.Net.Http.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Persistence;
using HR.Modules.Identity.Domain;
using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Persistence;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests;

/// <summary>
/// DSH-05: GET the manager team-status summary. The <c>employee:read</c> policy proves the caller
/// holds an administrative-read role; the browser-supplied <c>{managerId}</c> route value is then
/// authorized against the authenticated caller (self / above in the reporting tree / company-wide
/// employee access). Counts and drill-down are derived from one member list so they always agree.
/// </summary>
[Collection("Integration")]
public class GetManagerTeamStatusSummaryEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly LongAgo = new(2020, 1, 1);

    public GetManagerTeamStatusSummaryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static string Url(Guid companyId, Guid managerId) =>
        $"/api/companies/{companyId}/employees/{managerId}/team-status-summary";

    // ── auth matrix ────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(Url(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_For_Plain_Employee_Role()
    {
        var companyId = Guid.NewGuid();
        using var client = await ClientForAsync(companyId, Guid.NewGuid(), SystemRoles.Employee);

        var response = await client.GetAsync(Url(companyId, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Returns_Forbidden_When_Manager_Requests_An_Unrelated_Peer_Managers_Team()
    {
        var companyId = Guid.NewGuid();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var peerA = await SeedActiveEmployeeAsync(companyId, refData, "Peer", "A");
        var peerB = await SeedActiveEmployeeAsync(companyId, refData, "Peer", "B");
        await SeedActiveEmployeeAsync(companyId, refData, "Report", "OfB", managerId: peerB);

        using var client = await ClientForAsync(companyId, peerA, SystemRoles.Manager);

        var response = await client.GetAsync(Url(companyId, peerB));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manager_Gets_Ok_For_Their_Own_Team()
    {
        var companyId = Guid.NewGuid();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var manager = await SeedActiveEmployeeAsync(companyId, refData, "Mandy", "Manager");
        var report = await SeedActiveEmployeeAsync(companyId, refData, "Rita", "Report", managerId: manager);

        using var client = await ClientForAsync(companyId, manager, SystemRoles.Manager);
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId, manager));

        Assert.NotNull(payload);
        Assert.Equal(1, payload!.TeamSize);
        Assert.Contains(payload.Members, m => m.EmployeeId == report);
    }

    [Fact]
    public async Task SkipLevel_Manager_Gets_Ok_For_A_Subordinate_Managers_Team()
    {
        var companyId = Guid.NewGuid();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var senior = await SeedActiveEmployeeAsync(companyId, refData, "Sen", "Senior");
        var line = await SeedActiveEmployeeAsync(companyId, refData, "Lin", "Line", managerId: senior);
        var report = await SeedActiveEmployeeAsync(companyId, refData, "Ray", "Report", managerId: line);

        using var client = await ClientForAsync(companyId, senior, SystemRoles.Manager);
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId, line));

        Assert.NotNull(payload);
        Assert.Contains(payload!.Members, m => m.EmployeeId == report);
    }

    [Fact]
    public async Task HrAdministrator_Gets_Ok_For_Any_Managers_Team()
    {
        var companyId = Guid.NewGuid();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var manager = await SeedActiveEmployeeAsync(companyId, refData, "Ada", "Manager");
        var report = await SeedActiveEmployeeAsync(companyId, refData, "Ben", "Report", managerId: manager);

        using var client = await ClientForAsync(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId, manager));

        Assert.NotNull(payload);
        Assert.Equal(1, payload!.TeamSize);
        Assert.Contains(payload.Members, m => m.EmployeeId == report);
    }

    // ── company isolation ──────────────────────────────────────────────────

    [Fact]
    public async Task Company_Isolation_HrAdmin_For_Company_A_Never_Sees_Company_B_Team()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var refA = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyA);
        var refB = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyB);

        var managerA = await SeedActiveEmployeeAsync(companyA, refA, "Mgr", "A");
        await SeedActiveEmployeeAsync(companyA, refA, "Rep", "A1", managerId: managerA);

        var managerB = await SeedActiveEmployeeAsync(companyB, refB, "Mgr", "B");
        var reportB = await SeedActiveEmployeeAsync(companyB, refB, "Rep", "B1", managerId: managerB);

        using var client = await ClientForAsync(companyA, Guid.NewGuid(), SystemRoles.HrAdministrator);

        // Same manager-guid shape, but company A route: B's identically-structured tree must not leak.
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyA, managerA));

        Assert.NotNull(payload);
        Assert.Equal(1, payload!.TeamSize);
        Assert.DoesNotContain(payload.Members, m => m.EmployeeId == reportB);
        Assert.DoesNotContain(payload.Members, m => m.EmployeeId == managerB);
    }

    // ── counts + drill-down ────────────────────────────────────────────────

    [Fact]
    public async Task Counts_Are_Computed_End_To_End_And_Agree_With_The_Drilldown()
    {
        var companyId = Guid.NewGuid();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var manager = await SeedActiveEmployeeAsync(companyId, refData, "Boss", "Person");
        var onLeave = await SeedActiveEmployeeAsync(companyId, refData, "Leo", "OnLeave", managerId: manager);
        var offSick = await SeedActiveEmployeeAsync(companyId, refData, "Sid", "Sick", managerId: manager);
        var probation = await SeedActiveEmployeeAsync(companyId, refData, "Pam", "Probation", managerId: manager);
        var missingNote = await SeedActiveEmployeeAsync(companyId, refData, "Nia", "NoNote", managerId: manager);
        var plain = await SeedActiveEmployeeAsync(companyId, refData, "Amy", "AtWork", managerId: manager);

        // excluded from the counted population
        await SeedActiveEmployeeAsync(companyId, refData, "Fin", "Future", managerId: manager, startDate: Today.AddDays(30));
        await SeedActiveEmployeeAsync(companyId, refData, "Gus", "Gone", managerId: manager, leavingDate: Today.AddDays(-1));

        await SeedApprovedLeaveAsync(companyId, onLeave, Today.AddDays(-1), Today.AddDays(1));
        await SeedActiveSicknessAsync(companyId, offSick, Today.AddDays(-2));
        await SeedActiveProbationAsync(companyId, probation);
        await SeedPendingFitNoteAsync(companyId, missingNote); // on a closed record → not also "Sick"

        using var client = await ClientForAsync(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId, manager));

        Assert.NotNull(payload);
        // The manager is not part of their own reporting sub-tree: 5 reports are counted.
        Assert.Equal(5, payload!.TeamSize);
        Assert.DoesNotContain(payload.Members, m => m.EmployeeId == manager);
        Assert.Equal(1, payload.OnLeave);
        Assert.Equal(1, payload.Sick);
        Assert.Equal(1, payload.InProbation);
        Assert.Equal(1, payload.MissingFitNotes);
        Assert.Equal(2, payload.AwayToday); // leave + sick, distinct

        // drill-down parity — every headline equals the filtered member list
        Assert.Equal(payload.TeamSize, payload.Members.Count);
        Assert.Equal(payload.OnLeave, payload.Members.Count(m => m.OnLeaveToday));
        Assert.Equal(payload.Sick, payload.Members.Count(m => m.OffSickToday));
        Assert.Equal(payload.InProbation, payload.Members.Count(m => m.InProbation));
        Assert.Equal(payload.MissingFitNotes, payload.Members.Count(m => m.MissingFitNote));
        Assert.Equal(payload.AtWork, payload.Members.Count(m => m.PrimaryStatus == "AtWork"));
        Assert.Equal(payload.NotScheduledToday, payload.Members.Count(m => m.PrimaryStatus == "NotScheduled"));
        Assert.Equal(payload.AwayToday, payload.Members.Count(m => m.OnLeaveToday || m.OffSickToday));
        Assert.Equal(payload.TeamSize, payload.AtWork + payload.AwayToday + payload.NotScheduledToday);

        Assert.True(payload.Members.Single(m => m.EmployeeId == probation).InProbation);
        Assert.True(payload.Members.Single(m => m.EmployeeId == missingNote).MissingFitNote);
        Assert.False(payload.Members.Single(m => m.EmployeeId == missingNote).OffSickToday);
        Assert.Contains(payload.Members, m => m.EmployeeId == plain
            && !m.OnLeaveToday && !m.OffSickToday && !m.InProbation && !m.MissingFitNote);
    }

    [Fact]
    public async Task Leave_And_Sickness_Starting_Exactly_Today_Are_Counted()
    {
        var companyId = Guid.NewGuid();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var manager = await SeedActiveEmployeeAsync(companyId, refData, "Head", "Honcho");
        var leaveToday = await SeedActiveEmployeeAsync(companyId, refData, "Len", "LeaveToday", managerId: manager);
        var sickToday = await SeedActiveEmployeeAsync(companyId, refData, "Sam", "SickToday", managerId: manager);

        await SeedApprovedLeaveAsync(companyId, leaveToday, Today, Today);
        await SeedActiveSicknessAsync(companyId, sickToday, Today);

        using var client = await ClientForAsync(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId, manager));

        Assert.NotNull(payload);
        Assert.True(payload!.Members.Single(m => m.EmployeeId == leaveToday).OnLeaveToday);
        Assert.True(payload.Members.Single(m => m.EmployeeId == sickToday).OffSickToday);
        Assert.Equal(1, payload.OnLeave);
        Assert.Equal(1, payload.Sick);
    }

    [Fact]
    public async Task Not_Yet_Started_And_Already_Left_Employees_Are_Excluded_From_TeamSize()
    {
        var companyId = Guid.NewGuid();
        var refData = await EmployeeReferenceDataSeeder.SeedAsync(_factory, companyId);

        var manager = await SeedActiveEmployeeAsync(companyId, refData, "Only", "Boss");
        await SeedActiveEmployeeAsync(companyId, refData, "Not", "Started", managerId: manager, startDate: Today.AddDays(10));
        await SeedActiveEmployeeAsync(companyId, refData, "Al", "Ready", managerId: manager, leavingDate: Today.AddDays(-5));
        var current = await SeedActiveEmployeeAsync(companyId, refData, "Cur", "Rent", managerId: manager);

        using var client = await ClientForAsync(companyId, Guid.NewGuid(), SystemRoles.HrAdministrator);
        var payload = await client.GetFromJsonAsync<SummaryPayload>(Url(companyId, manager));

        Assert.NotNull(payload);
        Assert.Equal(1, payload!.TeamSize);
        Assert.Equal(current, payload.Members.Single().EmployeeId);
    }

    // ── helpers ────────────────────────────────────────────────────────────

    private async Task<HttpClient> ClientForAsync(Guid companyId, Guid userId, Guid roleId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, companyId.ToString());
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, roleId, companyId);
        await TestRoleSeeder.AssignRoleAsync(_factory, userId, SystemRoles.Employee, companyId);
        return client;
    }

    private async Task<Guid> SeedActiveEmployeeAsync(
        Guid companyId,
        EmployeeReferenceDataSeeder.ReferenceData refData,
        string firstName,
        string lastName,
        Guid? managerId = null,
        DateOnly? startDate = null,
        DateOnly? leavingDate = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EmployeesDbContext>();

        var employee = Employee.Create(
            Guid.NewGuid(), companyId, firstName, lastName,
            $"{firstName}.{lastName}.{Guid.NewGuid():N}@example.com".ToLowerInvariant(),
            startDate ?? LongAgo, hasSystemAccess: true, new DateOnly(1990, 1, 1), "British", "Prefer not to say",
            $"EMP-{Guid.NewGuid():N}", refData.EmploymentTypeId, refData.DepartmentId, refData.LocationId,
            refData.PositionProfileId, Now);

        employee.Assign(refData.DepartmentId, refData.PositionProfileId, refData.LocationId, managerId, Now);
        employee.Activate(Now);

        if (leavingDate is not null)
            employee.UpdateEmploymentDetails(
                employee.EmployeeNumber, employee.EmploymentTypeId, employee.StartDate,
                null, null, leavingDate, null, Now);

        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private async Task SeedApprovedLeaveAsync(Guid companyId, Guid employeeId, DateOnly start, DateOnly end)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LeaveDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var leaveType = LeaveType.Create(
            Guid.NewGuid(), companyId, $"Annual-{suffix}", $"AL{suffix}",
            25, AccrualMethod.Monthly, LeaveTypeBehaviour.Standard, Now);
        db.LeaveTypes.Add(leaveType);

        var request = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, Guid.NewGuid(),
            start, LeaveDayPart.FullDay, end, LeaveDayPart.FullDay, 1m, "Trip", Now,
            LeaveRequestStatus.Approved);
        db.LeaveRequests.Add(request);
        await db.SaveChangesAsync();
    }

    private async Task SeedActiveSicknessAsync(Guid companyId, Guid employeeId, DateOnly startDate)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SicknessDbContext>();

        var categoryId = Guid.NewGuid();
        db.SicknessCategories.Add(SicknessCategory.Create(categoryId, companyId, $"Illness-{categoryId:N}", 1, Now));
        db.SicknessRecords.Add(SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, categoryId, startDate, SicknessDayPart.FullDay,
            null, null, null, null, SicknessEvidenceStatus.NotRequired, Now));
        await db.SaveChangesAsync();
    }

    private async Task SeedActiveProbationAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbationDbContext>();

        db.ProbationRecords.Add(ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            Today.AddDays(-30), Today.AddDays(60), null, Today, Now));
        await db.SaveChangesAsync();
    }

    private async Task SeedPendingFitNoteAsync(Guid companyId, Guid employeeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SicknessDbContext>();

        var categoryId = Guid.NewGuid();
        db.SicknessCategories.Add(SicknessCategory.Create(categoryId, companyId, $"Illness-{categoryId:N}", 1, Now));

        // Closed record so this employee is "missing a fit note" without also being counted "Sick".
        var record = SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, categoryId, Today.AddDays(-20), SicknessDayPart.FullDay,
            Today.AddDays(-10), SicknessDayPart.FullDay, 8m, null, SicknessEvidenceStatus.Pending, Now);
        db.SicknessRecords.Add(record);
        db.SicknessEvidenceRequests.Add(SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, record.Id, Guid.Empty, Today.AddDays(-3), null, Now));
        await db.SaveChangesAsync();
    }

    private sealed record SummaryPayload(
        int TeamSize,
        int AtWork,
        int AwayToday,
        int OnLeave,
        int Sick,
        int InProbation,
        int MissingFitNotes,
        int NotScheduledToday,
        List<MemberPayload> Members);

    private sealed record MemberPayload(
        Guid EmployeeId,
        string FullName,
        string? JobTitle,
        bool OnLeaveToday,
        bool OffSickToday,
        bool InProbation,
        bool MissingFitNote,
        bool ScheduledToday,
        string PrimaryStatus);
}
