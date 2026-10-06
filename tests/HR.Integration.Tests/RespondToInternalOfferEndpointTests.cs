using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Identity.Domain;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Tasks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HR.Integration.Tests.Infrastructure.InternalAppointmentTestSeeder;
using static HR.Integration.Tests.Infrastructure.InternalOfferTestHelpers;

namespace HR.Integration.Tests;

/// <summary>
/// Internal vacancy offers: POST /api/companies/{c}/internal-offers/{applicationId}/response — the employee
/// accepts or declines their own offer. Records who/when/channel, completes the employee task, notifies the
/// hiring manager and the person who made the offer, and audits the response without salary.
/// 404 for another employee / non-internal / no offer; 409 for a stale version or a conflicting prior
/// decision; 400 for handler validation; 422 for a malformed body.
/// </summary>
[Collection("Integration")]
public class RespondToInternalOfferEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public RespondToInternalOfferEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Accept_Records_The_Response_Completes_The_Task_And_Notifies_The_Recruiters()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, recruiterId) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);

        var body = await RespondOkAsync(employee, s, "Accept");

        Assert.Equal(s.ApplicationId, body.GetProperty("applicationId").GetGuid());
        Assert.Equal(s.VacancyId, body.GetProperty("vacancyId").GetGuid());
        Assert.Equal(1, body.GetProperty("offerVersion").GetInt32());
        Assert.Equal("Accepted", body.GetProperty("offerResponseStatus").GetString());
        Assert.False(body.GetProperty("wasAlreadyRecorded").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("offerRespondedAt").ValueKind);

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(OfferResponseStatus.Accepted, application.OfferResponseStatus);
        Assert.Equal(s.EmployeeId, application.OfferRespondedByUserId);
        Assert.Equal(OfferResponseChannel.Employee, application.OfferResponseChannel);
        Assert.NotNull(application.OfferRespondedAt);
        Assert.True(application.OfferRespondedAt > before);
        Assert.Null(application.OfferResponseReason);

        var task = Assert.Single(await GetOfferTasksAsync(_factory, s));
        Assert.Equal(TaskItemStatus.Completed, task.Status);
        Assert.NotNull(task.CompletedAt);

        var hiringManagerNotices = await GetNotificationsAsync(_factory, companyId, s.World.NewManagerId, NotificationType.InternalOfferResponded);
        var recruiterNotices = await GetNotificationsAsync(_factory, companyId, recruiterId, NotificationType.InternalOfferResponded);
        var hiringManagerNotice = Assert.Single(hiringManagerNotices);
        var recruiterNotice = Assert.Single(recruiterNotices);
        Assert.Equal("Internal offer accepted", hiringManagerNotice.Title);
        Assert.Equal("Internal offer accepted", recruiterNotice.Title);
        Assert.Contains("accepted", hiringManagerNotice.Body);
        Assert.Contains("Engineering Manager", hiringManagerNotice.Body);
        Assert.Empty(await GetNotificationsAsync(_factory, companyId, s.EmployeeId, NotificationType.InternalOfferResponded));

        foreach (var notice in new[] { hiringManagerNotice, recruiterNotice })
            AssertNoSalary(notice.Title + " " + notice.Body);
    }

    [Fact]
    public async Task Accept_Audits_The_Response_With_Version_And_Channel_And_Without_Salary()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);

        await RespondOkAsync(employee, s, "Accept");

        var audit = Assert.Single(await GetAuditEventsAsync(_factory, companyId, s.ApplicationId, "offer.response_recorded"));
        Assert.Equal(s.EmployeeId, audit.ActorUserId);
        var metadata = MetadataOf(audit);
        Assert.Equal(1, metadata["offerVersion"].GetInt32());
        Assert.Equal("Employee", metadata["channel"].GetString());
        Assert.Contains("Accepted", audit.AfterJson);
        Assert.Contains("AwaitingResponse", audit.BeforeJson);
        AssertNoSalary(audit.BeforeJson + audit.AfterJson + audit.MetadataJson + audit.Summary);
    }

    [Fact]
    public async Task Decline_With_A_Reason_Records_The_Reason_And_Notifies()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, recruiterId) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);

        var body = await RespondOkAsync(employee, s, "Decline", reason: "  Not the right time.  ");

        Assert.Equal("Declined", body.GetProperty("offerResponseStatus").GetString());
        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(OfferResponseStatus.Declined, application.OfferResponseStatus);
        Assert.Equal("Not the right time.", application.OfferResponseReason);
        Assert.Equal(OfferResponseChannel.Employee, application.OfferResponseChannel);

        var task = Assert.Single(await GetOfferTasksAsync(_factory, s));
        Assert.Equal(TaskItemStatus.Completed, task.Status);

        var notice = Assert.Single(await GetNotificationsAsync(_factory, companyId, recruiterId, NotificationType.InternalOfferResponded));
        Assert.Equal("Internal offer declined", notice.Title);
        AssertNoSalary(notice.Title + " " + notice.Body);
        Assert.DoesNotContain("Not the right time", notice.Body);
    }

    [Fact]
    public async Task Decline_Without_A_Reason_Is_Accepted()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);

        await RespondOkAsync(employee, s, "decline");

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(OfferResponseStatus.Declined, application.OfferResponseStatus);
        Assert.Null(application.OfferResponseReason);
    }

    [Fact]
    public async Task Accept_Ignores_A_Supplied_Reason()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);

        await RespondOkAsync(employee, s, "Accept", reason: "Looking forward to it.");

        Assert.Null((await GetApplicationAsync(_factory, s.ApplicationId)).OfferResponseReason);
    }

    [Fact]
    public async Task Repeating_The_Same_Decision_Is_Idempotent_And_Creates_No_Duplicates()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, recruiterId) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);
        var first = await RespondOkAsync(employee, s, "Accept");

        var second = await RespondOkAsync(employee, s, "Accept");

        Assert.False(first.GetProperty("wasAlreadyRecorded").GetBoolean());
        Assert.True(second.GetProperty("wasAlreadyRecorded").GetBoolean());
        Assert.True(
            (first.GetProperty("offerRespondedAt").GetDateTimeOffset() - second.GetProperty("offerRespondedAt").GetDateTimeOffset()).Duration()
            < TimeSpan.FromMilliseconds(1));
        Assert.Single(await GetOfferTasksAsync(_factory, s));
        Assert.Single(await GetNotificationsAsync(_factory, companyId, recruiterId, NotificationType.InternalOfferResponded));
        Assert.Single(await GetNotificationsAsync(_factory, companyId, s.World.NewManagerId, NotificationType.InternalOfferResponded));
        Assert.Single(await GetAuditEventsAsync(_factory, companyId, s.ApplicationId, "offer.response_recorded"));
    }

    [Fact]
    public async Task Repeating_A_Decline_Is_Idempotent()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);
        await RespondOkAsync(employee, s, "Decline", reason: "No thanks");

        var second = await RespondOkAsync(employee, s, "Decline", reason: "No thanks");

        Assert.True(second.GetProperty("wasAlreadyRecorded").GetBoolean());
        Assert.Single(await GetAuditEventsAsync(_factory, companyId, s.ApplicationId, "offer.response_recorded"));
    }

    [Theory]
    [InlineData("Accept", "Decline")]
    [InlineData("Decline", "Accept")]
    public async Task The_Opposite_Decision_After_A_Response_Returns_Conflict_And_Changes_Nothing(string first, string opposite)
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);
        await RespondOkAsync(employee, s, first);
        var recorded = await GetApplicationAsync(_factory, s.ApplicationId);

        var response = await RespondAsync(employee, s, opposite);

        await AssertStatusAsync(HttpStatusCode.Conflict, response);
        var after = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(recorded.OfferResponseStatus, after.OfferResponseStatus);
        Assert.Equal(recorded.OfferRespondedAt, after.OfferRespondedAt);
        Assert.Single(await GetAuditEventsAsync(_factory, companyId, s.ApplicationId, "offer.response_recorded"));
    }

    [Fact]
    public async Task A_Stale_Offer_Version_Returns_Conflict_And_Records_Nothing()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);

        var response = await RespondAsync(employee, s, "Accept", offerVersion: 2);

        await AssertStatusAsync(HttpStatusCode.Conflict, response);
        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, application.OfferResponseStatus);
        Assert.Equal(TaskItemStatus.Open, Assert.Single(await GetOfferTasksAsync(_factory, s)).Status);
    }

    [Fact]
    public async Task Concurrent_Double_Accept_Records_One_Response_And_No_Duplicate_Effects()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, recruiterId) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employeeA = await EmployeeClientAsync(_factory, s);
        using var employeeB = await EmployeeClientAsync(_factory, s);

        var responses = await Task.WhenAll(
            RespondAsync(employeeA, s, "Accept"),
            RespondAsync(employeeB, s, "Accept"));

        var payloads = new List<JsonElement>();
        foreach (var response in responses)
        {
            await AssertStatusAsync(HttpStatusCode.OK, response);
            payloads.Add(await response.Content.ReadFromJsonAsync<JsonElement>());
        }

        Assert.Equal(1, payloads.Count(p => !p.GetProperty("wasAlreadyRecorded").GetBoolean()));
        Assert.Equal(1, payloads.Count(p => p.GetProperty("wasAlreadyRecorded").GetBoolean()));

        var task = Assert.Single(await GetOfferTasksAsync(_factory, s));
        Assert.Equal(TaskItemStatus.Completed, task.Status);
        Assert.Single(await GetNotificationsAsync(_factory, companyId, recruiterId, NotificationType.InternalOfferResponded));
        Assert.Single(await GetNotificationsAsync(_factory, companyId, s.World.NewManagerId, NotificationType.InternalOfferResponded));
        Assert.Single(await GetAuditEventsAsync(_factory, companyId, s.ApplicationId, "offer.response_recorded"));
    }

    [Fact]
    public async Task Another_Employee_Gets_NotFound_And_Cannot_Respond()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var other = await ClientForUserAsync(_factory, companyId, Guid.NewGuid(), SystemRoles.Employee);

        var response = await RespondAsync(other, s, "Accept");

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferResponseStatus);
    }

    [Fact]
    public async Task A_Recruiter_Who_Is_Not_The_Recipient_Gets_NotFound()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);

        var response = await RespondAsync(recruiter, s, "Accept");

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferResponseStatus);
    }

    [Fact]
    public async Task Returns_NotFound_When_No_Offer_Has_Been_Made()
    {
        var companyId = Guid.NewGuid();
        var s = await SeedUnofferedAsync(_factory, companyId);
        using var employee = await EmployeeClientAsync(_factory, s);

        var response = await RespondAsync(employee, s, "Accept");

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Returns_NotFound_For_An_External_Application()
    {
        var companyId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        await TestRoleSeeder.AssignRoleAsync(_factory, employeeUserId, SystemRoles.Employee, companyId);
        var s = await SeedAsync(_factory, companyId, source: ApplicationSource.Direct, employeeId: employeeUserId);
        using var employee = await EmployeeClientAsync(_factory, s);

        var response = await RespondAsync(employee, s, "Accept");

        await AssertStatusAsync(HttpStatusCode.NotFound, response);
    }

    [Fact]
    public async Task Anonymous_Returns_Unauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/internal-offers/{Guid.NewGuid()}/response",
            new { decision = "Accept", offerVersion = 1 });

        await AssertStatusAsync(HttpStatusCode.Unauthorized, response);
    }

    [Fact]
    public async Task Another_Companys_Route_Returns_Forbidden()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);

        var response = await employee.PostAsJsonAsync(
            $"/api/companies/{Guid.NewGuid()}/internal-offers/{s.ApplicationId}/response",
            new { decision = "Accept", offerVersion = 1 });

        await AssertStatusAsync(HttpStatusCode.Forbidden, response);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferResponseStatus);
    }

    [Theory]
    [InlineData("Maybe", 1)]
    [InlineData("", 1)]
    [InlineData("Accept", 0)]
    [InlineData("Accept", -1)]
    public async Task An_Invalid_Body_Returns_422_And_Records_Nothing(string decision, int offerVersion)
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);

        var response = await RespondAsync(employee, s, decision, offerVersion);

        await AssertStatusAsync(HttpStatusCode.UnprocessableEntity, response);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferResponseStatus);
    }

    [Fact]
    public async Task A_Reason_Of_1000_Characters_Is_Accepted()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);

        await RespondOkAsync(employee, s, "Decline", reason: new string('r', 1000));

        Assert.Equal(1000, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferResponseReason!.Length);
    }

    [Fact]
    public async Task A_Reason_Of_1001_Characters_Returns_422()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);

        var response = await RespondAsync(employee, s, "Decline", reason: new string('r', 1001));

        await AssertStatusAsync(HttpStatusCode.UnprocessableEntity, response);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferResponseStatus);
    }

    [Fact]
    public async Task A_Withdrawn_Application_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);
        await AssertStatusAsync(HttpStatusCode.OK, await recruiter.DeleteAsync(ApplicationUrl(s)));

        var response = await RespondAsync(employee, s, "Accept");

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        Assert.NotEqual(OfferResponseStatus.Accepted, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferResponseStatus);
    }

    [Fact]
    public async Task An_Employee_Who_Is_No_Longer_Active_Returns_BadRequest()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HR.Modules.Employees.Persistence.EmployeesDbContext>();
            var row = await db.Employees.SingleAsync(e => e.Id == s.EmployeeId);
            row.Suspend(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }

        var response = await RespondAsync(employee, s, "Accept");

        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferResponseStatus);
    }

    [Fact]
    public async Task Responding_After_An_Appointment_Has_Started_Is_Refused()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter, b => b["proposedStartDate"] = Today.ToString("yyyy-MM-dd"));
        using var employee = await EmployeeClientAsync(_factory, s);
        await RespondOkAsync(employee, s, "Accept");
        await AssertStatusAsync(HttpStatusCode.OK, await recruiter.PostAsJsonAsync(AppointUrl(s), new { }));

        var response = await RespondAsync(employee, s, "Decline");

        await AssertStatusAsync(HttpStatusCode.Conflict, response);
        Assert.Equal(OfferResponseStatus.Accepted, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferResponseStatus);
    }

    private static void AssertNoSalary(string text)
    {
        Assert.DoesNotContain("75000", text);
        Assert.DoesNotContain("75,000", text);
        Assert.DoesNotContain(DefaultCurrency, text);
        Assert.DoesNotContain("£", text);
    }
}
