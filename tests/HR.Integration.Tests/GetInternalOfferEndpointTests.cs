using System.Net;
using System.Text.Json;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using static HR.Integration.Tests.Infrastructure.InternalAppointmentTestSeeder;
using static HR.Integration.Tests.Infrastructure.InternalOfferTestHelpers;

namespace HR.Integration.Tests;

/// <summary>
/// Internal vacancy offers: GET /api/companies/{c}/internal-offers/{applicationId} — the signed-in employee
/// reviews the full snapshotted terms of an offer made to them. 404 for anyone who is neither the linked
/// employee nor a holder of recruitment:manage (who may view but never respond).
/// </summary>
[Collection("Integration")]
public class GetInternalOfferEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public GetInternalOfferEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_Returns_The_Full_Offer_Snapshot_For_The_Linked_Employee()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);
        await SetTargetProfileAsync(_factory, s,
            workingDays: HR.Modules.Employees.Contracts.WorkingDays.Monday | HR.Modules.Employees.Contracts.WorkingDays.Wednesday,
            hoursPerDay: 7.5m, probationMonths: 6);
        var start = Today.AddDays(21);
        var deadline = Today.AddDays(5);
        await MakeOfferOkAsync(recruiter, s, b =>
        {
            b["proposedStartDate"] = start.ToString("yyyy-MM-dd");
            b["responseDeadline"] = deadline.ToString("yyyy-MM-dd");
        });
        using var employee = await EmployeeClientAsync(_factory, s);

        var body = await GetOfferOkAsync(employee, s);

        Assert.Equal(s.ApplicationId, body.GetProperty("applicationId").GetGuid());
        Assert.Equal(s.VacancyId, body.GetProperty("vacancyId").GetGuid());
        Assert.True(body.GetProperty("isOfferRecipient").GetBoolean());
        Assert.True(body.GetProperty("canRespond").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("cannotRespondReason").ValueKind);
        Assert.NotEmpty(body.GetProperty("internalAppointmentNotices").EnumerateArray());

        var terms = body.GetProperty("terms");
        Assert.Equal(1, terms.GetProperty("offerVersion").GetInt32());
        Assert.Equal("AwaitingResponse", terms.GetProperty("offerResponseStatus").GetString());
        Assert.Equal("Engineering Manager", terms.GetProperty("jobTitle").GetString());
        Assert.False(string.IsNullOrEmpty(terms.GetProperty("departmentName").GetString()));
        Assert.False(string.IsNullOrEmpty(terms.GetProperty("locationName").GetString()));
        Assert.False(string.IsNullOrEmpty(terms.GetProperty("employmentTypeName").GetString()));
        Assert.Equal("Nathan Brooks", terms.GetProperty("proposedManagerName").GetString());
        Assert.False(terms.GetProperty("noManager").GetBoolean());
        Assert.Equal(DefaultSalary, terms.GetProperty("salary").GetDecimal());
        Assert.Equal("Annual", terms.GetProperty("salaryFrequency").GetString());
        Assert.Equal(DefaultCurrency, terms.GetProperty("currency").GetString());
        Assert.Equal(start.ToString("yyyy-MM-dd"), terms.GetProperty("proposedStartDate").GetString());
        Assert.Equal(["Monday", "Wednesday"], terms.GetProperty("workingDays").EnumerateArray().Select(d => d.GetString()).ToArray());
        Assert.Equal(7.5m, terms.GetProperty("hoursPerDay").GetDecimal());
        Assert.Equal(37.5m, terms.GetProperty("hoursPerWeek").GetDecimal());
        Assert.Equal(1m, terms.GetProperty("fte").GetDecimal());
        Assert.Equal(6, terms.GetProperty("probationMonths").GetInt32());
        Assert.Equal(Today.ToString("yyyy-MM-dd"), terms.GetProperty("offerDate").GetString());
        Assert.Equal(deadline.ToString("yyyy-MM-dd"), terms.GetProperty("responseDeadline").GetString());
        Assert.Equal("Welcome to the new team.", terms.GetProperty("offerNotes").GetString());
        Assert.True(terms.GetProperty("isSnapshotComplete").GetBoolean());
    }

    [Fact]
    public async Task Get_Is_Unaffected_By_A_Later_Change_To_The_Position_Profile()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);
        await SetTargetProfileAsync(_factory, s,
            salaryMin: 55000m,
            workingDays: HR.Modules.Employees.Contracts.WorkingDays.Monday | HR.Modules.Employees.Contracts.WorkingDays.Tuesday | HR.Modules.Employees.Contracts.WorkingDays.Friday,
            hoursPerDay: 7.5m, probationMonths: 6);
        await MakeOfferOkAsync(recruiter, s, b =>
        {
            b.Remove("hoursPerWeek");
            b.Remove("offeredSalary");
        });
        using var employee = await EmployeeClientAsync(_factory, s);
        var before = await GetOfferOkAsync(employee, s);

        await SetTargetProfileAsync(_factory, s,
            salaryMin: 99000m,
            workingDays: HR.Modules.Employees.Contracts.WorkingDays.Thursday,
            hoursPerDay: 4m, probationMonths: 1,
            title: "Renamed Profile",
            moveToCurrentDepartmentAndLocation: true,
            renameTargetDepartmentTo: $"Renamed-{Guid.NewGuid():N}");
        var after = await GetOfferOkAsync(employee, s);

        Assert.Equal(before.GetProperty("terms").GetRawText(), after.GetProperty("terms").GetRawText());
        var terms = after.GetProperty("terms");
        Assert.Equal(55000m, terms.GetProperty("salary").GetDecimal());
        Assert.Equal(["Monday", "Tuesday", "Friday"], terms.GetProperty("workingDays").EnumerateArray().Select(d => d.GetString()).ToArray());
        Assert.Equal(22.5m, terms.GetProperty("hoursPerWeek").GetDecimal());
        Assert.Equal(6, terms.GetProperty("probationMonths").GetInt32());
    }

    [Fact]
    public async Task Get_Returns_NotFound_For_Another_Employee()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var other = await ClientForUserAsync(_factory, companyId, Guid.NewGuid(), SystemRoles.Employee);

        var response = await other.GetAsync(InternalOfferUrl(s));

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Get_Returns_NotFound_When_No_Offer_Has_Been_Made()
    {
        var companyId = Guid.NewGuid();
        var s = await SeedUnofferedAsync(_factory, companyId);
        using var employee = await EmployeeClientAsync(_factory, s);

        var response = await employee.GetAsync(InternalOfferUrl(s));

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Get_Returns_NotFound_For_An_Unknown_Application()
    {
        var companyId = Guid.NewGuid();
        var s = await SeedUnofferedAsync(_factory, companyId);
        using var employee = await EmployeeClientAsync(_factory, s);

        var response = await employee.GetAsync(InternalOfferUrl(companyId, Guid.NewGuid()));

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Get_Returns_NotFound_For_An_External_Application()
    {
        var companyId = Guid.NewGuid();
        var s = await SeedAsync(_factory, companyId, source: ApplicationSource.Direct);
        using var recruiterClient = await RecruiterHrClientAsync(_factory, companyId);

        var response = await recruiterClient.GetAsync(InternalOfferUrl(s));

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Get_To_Another_Companys_Route_Returns_Forbidden()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);

        var response = await employee.GetAsync(InternalOfferUrl(Guid.NewGuid(), s.ApplicationId));

        await AssertStatusAsync(HttpStatusCode.Forbidden, response);
    }

    [Fact]
    public async Task Get_Returns_NotFound_For_An_Employee_Of_Another_Company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var outsider = await ClientForUserAsync(_factory, otherCompanyId, Guid.NewGuid(), SystemRoles.Employee);

        var response = await outsider.GetAsync(InternalOfferUrl(otherCompanyId, s.ApplicationId));

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Get_Returns_Unauthorized_For_Anonymous()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(InternalOfferUrl(Guid.NewGuid(), Guid.NewGuid()));

        await AssertStatusAsync(HttpStatusCode.Unauthorized, response);
    }

    [Fact]
    public async Task Get_By_A_Recruiter_Shows_The_Offer_But_Cannot_Respond()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);

        var body = await GetOfferOkAsync(recruiter, s);

        Assert.False(body.GetProperty("isOfferRecipient").GetBoolean());
        Assert.False(body.GetProperty("canRespond").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("cannotRespondReason").GetString()));
        Assert.Equal(DefaultSalary, body.GetProperty("terms").GetProperty("salary").GetDecimal());
    }

    [Fact]
    public async Task Get_Returns_NotFound_For_An_Employee_Without_Recruitment_Manage_Who_Is_Not_The_Recipient()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var hrOnly = await ClientForUserAsync(_factory, companyId, Guid.NewGuid(), SystemRoles.HrAdministrator, SystemRoles.Employee);

        var response = await hrOnly.GetAsync(InternalOfferUrl(s));

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Get_After_Acceptance_Reports_The_Response_And_That_The_Employee_Can_No_Longer_Respond()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);
        await RespondOkAsync(employee, s, "Accept");

        var body = await GetOfferOkAsync(employee, s);

        Assert.False(body.GetProperty("canRespond").GetBoolean());
        Assert.Contains("accepted", body.GetProperty("cannotRespondReason").GetString());
        var terms = body.GetProperty("terms");
        Assert.Equal("Accepted", terms.GetProperty("offerResponseStatus").GetString());
        Assert.Equal("Employee", terms.GetProperty("responseChannel").GetString());
        Assert.Equal(s.EmployeeId, terms.GetProperty("respondedByUserId").GetGuid());
        Assert.NotEqual(JsonValueKind.Null, terms.GetProperty("offerRespondedAt").ValueKind);
    }

    [Fact]
    public async Task Get_For_A_Withdrawn_Application_Reports_That_It_Cannot_Be_Responded_To()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);
        var withdraw = await recruiter.DeleteAsync(ApplicationUrl(s));
        await AssertStatusAsync(HttpStatusCode.OK, withdraw);

        var body = await GetOfferOkAsync(employee, s);

        Assert.False(body.GetProperty("canRespond").GetBoolean());
        Assert.Equal("This application has been withdrawn.", body.GetProperty("cannotRespondReason").GetString());
    }

    [Fact]
    public async Task Get_For_A_Legacy_Offer_Without_A_Snapshot_Is_Viewable_And_Flagged_Incomplete()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee, companyId);
        var s = await SeedAsync(_factory, companyId, employeeId: employeeUserId);
        using var employee = await EmployeeClientAsync(_factory, s);

        var body = await GetOfferOkAsync(employee, s);

        Assert.True(body.GetProperty("isOfferRecipient").GetBoolean());
        var terms = body.GetProperty("terms");
        Assert.False(terms.GetProperty("isSnapshotComplete").GetBoolean());
        Assert.Equal(1, terms.GetProperty("offerVersion").GetInt32());
        Assert.Equal("Accepted", terms.GetProperty("offerResponseStatus").GetString());
        Assert.Equal(72000m, terms.GetProperty("salary").GetDecimal());
        Assert.Equal(JsonValueKind.Null, terms.GetProperty("currency").ValueKind);
    }
}
