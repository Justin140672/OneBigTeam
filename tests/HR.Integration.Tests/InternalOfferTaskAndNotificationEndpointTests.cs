using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Integration.Tests.Infrastructure;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using HR.Modules.Tasks.Contracts;
using HR.Modules.Tasks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static HR.Integration.Tests.Infrastructure.InternalAppointmentTestSeeder;
using static HR.Integration.Tests.Infrastructure.InternalOfferTestHelpers;

namespace HR.Integration.Tests;

/// <summary>
/// Internal vacancy offers: making an internal offer creates exactly one Tasks-module task for the linked
/// employee (plus the standard TaskAssigned notification, without salary); the task is completed or
/// cancelled by every route that resolves the offer (employee response, recruiter fallback response, offer
/// withdrawn, application withdrawn or rejected, offer revised); and effect delivery is idempotent.
/// </summary>
[Collection("Integration")]
public class InternalOfferTaskAndNotificationEndpointTests
{
    private readonly ApiWebApplicationFactory _factory;

    public InternalOfferTaskAndNotificationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Offer_Creates_Exactly_One_Task_For_The_Linked_Employee()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, recruiterId) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);
        var deadline = Today.AddDays(9);

        await MakeOfferOkAsync(recruiter, s, b => b["responseDeadline"] = deadline.ToString("yyyy-MM-dd"));

        var task = Assert.Single(await GetOfferTasksAsync(_factory, s));
        Assert.Equal(TaskSource.Recruitment, task.Source);
        Assert.Equal(TaskActionType.Approve, task.ActionType);
        Assert.Equal(s.ApplicationId, task.SourceEntityId);
        Assert.Equal(s.EmployeeId, task.AssignedEmployeeId);
        Assert.Equal(s.EmployeeId, task.AssignedUserId);
        Assert.Equal(TaskItemStatus.Open, task.Status);
        Assert.Equal(deadline, task.DueDate);
        Assert.Equal(recruiterId, task.CreatedBy);
        Assert.Equal("Review your internal job offer — Engineering Manager", task.Title);
        Assert.Equal($"recruitment-internal-offer:{s.ApplicationId}:1", task.IdempotencyKey);
        AssertNoSalary(task.Title + " " + task.Description);
    }

    [Fact]
    public async Task Offer_Without_A_Response_Deadline_Creates_A_Task_Without_A_Due_Date()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        await MakeOfferOkAsync(recruiter, s, b => b.Remove("responseDeadline"));

        Assert.Null(Assert.Single(await GetOfferTasksAsync(_factory, s)).DueDate);
    }

    [Fact]
    public async Task Offer_Sends_Exactly_One_TaskAssigned_Notification_Without_Salary()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);

        await MakeOfferOkAsync(recruiter, s);

        var task = Assert.Single(await GetOfferTasksAsync(_factory, s));
        var notice = Assert.Single(await GetTaskAssignedNotificationsAsync(_factory, s, task.Id));
        Assert.Equal(s.EmployeeId, notice.EmployeeId);
        Assert.Contains("Review your internal job offer", notice.Title);
        AssertNoSalary(notice.Title + " " + notice.Body);
        Assert.Single(await GetNotificationsAsync(_factory, companyId, s.EmployeeId, NotificationType.TaskAssigned));
    }

    [Fact]
    public async Task The_Employee_Sees_The_Task_In_My_Tasks()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        using var employee = await EmployeeClientAsync(_factory, s);

        var body = await employee.GetFromJsonAsync<JsonElement>($"/api/companies/{companyId}/tasks/my");

        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal("Review your internal job offer — Engineering Manager", item.GetProperty("title").GetString());
        Assert.Equal("Recruitment", item.GetProperty("source").GetString());
        Assert.Equal("Approve", item.GetProperty("actionType").GetString());
        Assert.Equal("Open", item.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Reposting_The_Offer_With_The_Same_Idempotency_Key_Creates_No_Duplicate_Task_Or_Notification()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedUnofferedAsync(_factory, companyId);
        var key = Guid.NewGuid().ToString();

        var first = await MakeOfferOkAsync(recruiter, s, idempotencyKey: key);
        var second = await MakeOfferOkAsync(recruiter, s, idempotencyKey: key);

        Assert.Equal(first.GetProperty("offerTerms").GetRawText(), second.GetProperty("offerTerms").GetRawText());
        Assert.Equal(1, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferVersion);
        var task = Assert.Single(await GetOfferTasksAsync(_factory, s));
        Assert.Single(await GetTaskAssignedNotificationsAsync(_factory, s, task.Id));
    }

    [Fact]
    public async Task Running_The_Effects_Again_Creates_No_Duplicate_Task_Or_Notification()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);

        using (var scope = _factory.Services.CreateScope())
        {
            var effects = scope.ServiceProvider.GetRequiredService<InternalOfferTaskEffectsService>();
            await effects.RunOutstandingForApplicationAsync(companyId, s.ApplicationId, CancellationToken.None);
            await effects.RunOutstandingForApplicationAsync(companyId, s.ApplicationId, CancellationToken.None);
        }

        var task = Assert.Single(await GetOfferTasksAsync(_factory, s));
        Assert.Single(await GetTaskAssignedNotificationsAsync(_factory, s, task.Id));
    }

    [Fact]
    public async Task Reconciliation_Creates_The_Task_When_The_Post_Commit_Delivery_Never_Ran()
    {
        var companyId = Guid.NewGuid();
        var s = await SeedUnofferedAsync(_factory, companyId);
        var recruiterId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentDbContext>();
            var application = await db.Applications.SingleAsync(a => a.Id == s.ApplicationId);
            application.RecordOfferTerms(70000m, OfferSalaryFrequency.Annual, Today.AddDays(14), Today, null, now, null, recruiterId);
            db.InternalOfferTaskEffects.Add(InternalOfferTaskEffect.Create(
                Guid.NewGuid(), companyId, s.ApplicationId, application.OfferVersion, s.EmployeeId, recruiterId,
                "Engineering Manager", Today.AddDays(7), now));
            await db.SaveChangesAsync();
        }
        Assert.Empty(await GetOfferTasksAsync(_factory, s));

        for (var run = 0; run < 2; run++)
        {
            using var scope = _factory.Services.CreateScope();
            var effects = scope.ServiceProvider.GetRequiredService<InternalOfferTaskEffectsService>();
            await effects.RunOutstandingForApplicationAsync(companyId, s.ApplicationId, CancellationToken.None);
        }

        var task = Assert.Single(await GetOfferTasksAsync(_factory, s));
        Assert.Equal(TaskItemStatus.Open, task.Status);
        Assert.Equal(s.EmployeeId, task.AssignedEmployeeId);
        Assert.Single(await GetTaskAssignedNotificationsAsync(_factory, s, task.Id));
    }

    [Fact]
    public async Task Recruiter_Fallback_Accept_Completes_The_Same_Task_Without_A_Recruiter_Notification()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, recruiterId) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);

        var response = await RecruiterRespondAsync(recruiter, s, "Accepted");

        await AssertStatusAsync(HttpStatusCode.OK, response);
        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(OfferResponseStatus.Accepted, application.OfferResponseStatus);
        Assert.Equal(OfferResponseChannel.Recruiter, application.OfferResponseChannel);
        Assert.Equal(recruiterId, application.OfferRespondedByUserId);
        Assert.Equal(TaskItemStatus.Completed, Assert.Single(await GetOfferTasksAsync(_factory, s)).Status);
        Assert.Empty(await GetNotificationsAsync(_factory, companyId, recruiterId, NotificationType.InternalOfferResponded));
        Assert.Empty(await GetNotificationsAsync(_factory, companyId, s.World.NewManagerId, NotificationType.InternalOfferResponded));

        var audit = Assert.Single(await GetAuditEventsAsync(_factory, companyId, s.ApplicationId, "offer.response_recorded"));
        Assert.Equal("Recruiter", MetadataOf(audit)["channel"].GetString());
    }

    [Fact]
    public async Task Recruiter_Fallback_Decline_Completes_The_Task()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);

        await AssertStatusAsync(HttpStatusCode.OK, await RecruiterRespondAsync(recruiter, s, "Declined", "Told us by phone."));

        var application = await GetApplicationAsync(_factory, s.ApplicationId);
        Assert.Equal(OfferResponseStatus.Declined, application.OfferResponseStatus);
        Assert.Equal(OfferResponseChannel.Recruiter, application.OfferResponseChannel);
        Assert.Equal(TaskItemStatus.Completed, Assert.Single(await GetOfferTasksAsync(_factory, s)).Status);
    }

    [Fact]
    public async Task Withdrawing_The_Offer_Cancels_The_Employee_Task()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);

        await AssertStatusAsync(HttpStatusCode.OK, await RecruiterRespondAsync(recruiter, s, "Withdrawn"));

        Assert.Equal(OfferResponseStatus.Withdrawn, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferResponseStatus);
        Assert.Equal(TaskItemStatus.Cancelled, Assert.Single(await GetOfferTasksAsync(_factory, s)).Status);
    }

    [Fact]
    public async Task Withdrawing_The_Application_Cancels_The_Employee_Task()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);

        await AssertStatusAsync(HttpStatusCode.OK, await recruiter.DeleteAsync(ApplicationUrl(s)));

        Assert.NotNull((await GetApplicationAsync(_factory, s.ApplicationId)).WithdrawnAt);
        Assert.Equal(TaskItemStatus.Cancelled, Assert.Single(await GetOfferTasksAsync(_factory, s)).Status);
    }

    [Fact]
    public async Task Rejecting_The_Application_Cancels_The_Employee_Task()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);

        var response = await recruiter.PostAsJsonAsync($"{ApplicationUrl(s)}/reject", new { rejectionReason = "Position filled." });

        await AssertStatusAsync(HttpStatusCode.OK, response);
        Assert.Equal(TaskItemStatus.Cancelled, Assert.Single(await GetOfferTasksAsync(_factory, s)).Status);
    }

    [Fact]
    public async Task A_Revised_Offer_Cancels_The_Superseded_Task_And_Creates_A_New_One()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        var newDeadline = Today.AddDays(12);

        var revised = await MakeOfferOkAsync(recruiter, s, b =>
        {
            b["offeredSalary"] = 80000m;
            b["responseDeadline"] = newDeadline.ToString("yyyy-MM-dd");
        });

        Assert.Equal(2, revised.GetProperty("offerTerms").GetProperty("offerVersion").GetInt32());
        Assert.Equal(2, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferVersion);

        var tasks = await GetOfferTasksAsync(_factory, s);
        Assert.Equal(2, tasks.Count);
        var superseded = Assert.Single(tasks, t => t.IdempotencyKey == $"recruitment-internal-offer:{s.ApplicationId}:1");
        var current = Assert.Single(tasks, t => t.IdempotencyKey == $"recruitment-internal-offer:{s.ApplicationId}:2");
        Assert.Equal(TaskItemStatus.Cancelled, superseded.Status);
        Assert.Equal(TaskItemStatus.Open, current.Status);
        Assert.Equal(newDeadline, current.DueDate);
        Assert.Equal(s.EmployeeId, current.AssignedEmployeeId);

        Assert.Single(await GetTaskAssignedNotificationsAsync(_factory, s, current.Id));
    }

    [Fact]
    public async Task After_A_Revised_Offer_The_Employee_Must_Respond_To_Version_Two()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter);
        await MakeOfferOkAsync(recruiter, s, b => b["offeredSalary"] = 80000m);
        using var employee = await EmployeeClientAsync(_factory, s);

        var stale = await RespondAsync(employee, s, "Accept", offerVersion: 1);
        var current = await RespondOkAsync(employee, s, "Accept", offerVersion: 2);

        await AssertStatusAsync(HttpStatusCode.Conflict, stale);
        Assert.Equal(2, current.GetProperty("offerVersion").GetInt32());
        var tasks = await GetOfferTasksAsync(_factory, s);
        Assert.Equal(TaskItemStatus.Cancelled, tasks.Single(t => t.IdempotencyKey!.EndsWith(":1")).Status);
        Assert.Equal(TaskItemStatus.Completed, tasks.Single(t => t.IdempotencyKey!.EndsWith(":2")).Status);
    }

    [Fact]
    public async Task A_Revised_Offer_Is_Refused_Once_The_Appointment_Has_Started()
    {
        var companyId = Guid.NewGuid();
        var (recruiter, _) = await RecruiterAsync(_factory, companyId);
        using var _ = recruiter;
        var s = await SeedOfferedAsync(_factory, companyId, recruiter, b => b["proposedStartDate"] = Today.ToString("yyyy-MM-dd"));
        using var employee = await EmployeeClientAsync(_factory, s);
        await RespondOkAsync(employee, s, "Accept");
        await AssertStatusAsync(HttpStatusCode.OK, await recruiter.PostAsJsonAsync(AppointUrl(s), new { }));

        var response = await MakeOfferAsync(recruiter, s);

        await AssertStatusAsync(HttpStatusCode.Conflict, response);
        Assert.Equal(1, (await GetApplicationAsync(_factory, s.ApplicationId)).OfferVersion);
    }

    private static void AssertNoSalary(string? text)
    {
        Assert.DoesNotContain("75000", text ?? string.Empty);
        Assert.DoesNotContain("75,000", text ?? string.Empty);
        Assert.DoesNotContain(DefaultCurrency, text ?? string.Empty);
        Assert.DoesNotContain("£", text ?? string.Empty);
    }
}
