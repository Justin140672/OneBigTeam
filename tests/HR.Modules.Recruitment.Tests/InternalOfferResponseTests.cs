using HR.Infrastructure.Abstractions;
using HR.Modules.Employees.Contracts;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Features.RespondToOffer;
using HR.Modules.Recruitment.Features.WithdrawApplication;
using HR.Modules.Recruitment.Services;
using HR.Modules.Recruitment.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class InternalOfferResponseTests
{
    [Fact]
    public async Task Offer_Creates_One_Task_With_Title_Due_Date_Source_And_Application_Link_And_Standard_Notification()
    {
        var h = await InternalOfferHarness.CreateAsync();

        await h.OfferAsync();

        var task = Assert.Single(h.Wiring.Creator.Created);
        Assert.Equal("Review your internal job offer — Engineering Manager", task.Title);
        Assert.Equal(TaskSource.Recruitment, task.Source);
        Assert.Equal(TaskActionType.Approve, task.ActionType);
        Assert.Equal(h.EmployeeId, task.AssignedEmployeeId);
        Assert.Equal(h.Application.Id, task.SourceEntityId);
        Assert.Equal(new DateOnly(2026, 10, 20), task.DueDate);
        Assert.True(task.NotifyAssignee);
        Assert.DoesNotContain("72000", task.Title + task.Description);
        Assert.DoesNotContain("GBP", task.Title + task.Description);
    }

    [Fact]
    public async Task Offer_Without_A_Deadline_Creates_A_Task_Without_A_Due_Date()
    {
        var h = await InternalOfferHarness.CreateAsync();

        await h.OfferAsync(h.ValidOffer() with { ResponseDeadline = null });

        Assert.Null(Assert.Single(h.Wiring.Creator.Created).DueDate);
    }

    [Fact]
    public async Task Repeated_Effect_Delivery_Does_Not_Create_Duplicate_Tasks()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();

        await h.Wiring.Effects.RunOutstandingForApplicationAsync(h.CompanyId, h.Application.Id, CancellationToken.None);
        await h.Wiring.Effects.RunAllOutstandingAsync(CancellationToken.None);

        Assert.Single(h.Wiring.Creator.Created);
    }

    [Fact]
    public async Task Task_Creation_Failure_Leaves_The_Effect_Outstanding_And_Is_Recovered_On_Reconciliation()
    {
        var h = await InternalOfferHarness.CreateAsync();
        h.Wiring.Creator.FailBeforePersist = true;

        var saved = await h.OfferAsync();

        Assert.Equal(OfferResponseStatus.AwaitingResponse, saved.OfferResponseStatus);
        var failed = await h.Db.InternalOfferTaskEffects.SingleAsync();
        Assert.Null(failed.TaskCreatedAt);
        Assert.NotNull(failed.FailureReason);
        Assert.Empty(h.Wiring.Creator.Created);

        h.Wiring.Creator.FailBeforePersist = false;
        await h.Wiring.Effects.RunAllOutstandingAsync(CancellationToken.None);

        Assert.Single(h.Wiring.Creator.Created);
        Assert.NotNull((await h.Db.InternalOfferTaskEffects.SingleAsync()).TaskCreatedAt);
    }

    [Fact]
    public async Task Employee_Accepts_Offer_Records_Who_And_When_Completes_The_Task_And_Notifies_Recruiters()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();
        var offeredBy = (await h.ReloadAsync()).OfferMadeByUserId!.Value;

        var result = await h.RespondHandler().HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("Accepted", result.Value!.OfferResponseStatus);
        Assert.False(result.Value.WasAlreadyRecorded);

        var saved = await h.ReloadAsync();
        Assert.Equal(OfferResponseStatus.Accepted, saved.OfferResponseStatus);
        Assert.Equal(h.EmployeeId, saved.OfferRespondedByUserId);
        Assert.Equal(OfferResponseChannel.Employee, saved.OfferResponseChannel);
        Assert.Equal(InternalOfferHarness.Now, saved.OfferRespondedAt);

        var completion = Assert.Single(h.Wiring.Completer.Calls);
        Assert.Equal(h.Application.Id, completion.SourceEntityId);
        Assert.Equal(TaskSource.Recruitment, completion.Source);
        Assert.Equal(TaskActionType.Approve, completion.ActionType);
        Assert.Equal(h.EmployeeId, completion.CompletedBy);
        Assert.Equal([TaskCompletionDispatchMode.BusinessEffectAlreadyApplied], h.Wiring.Resolution.CompletionModes);

        var recipients = h.Wiring.Notifications.Written
            .Where(n => n.Type == NotificationType.InternalOfferResponded).Select(n => n.EmployeeId).ToList();
        Assert.Contains(h.HiringManagerId, recipients);
        Assert.Contains(offeredBy, recipients);
        Assert.Equal(recipients.Count, recipients.Distinct().Count());
    }

    [Fact]
    public async Task Notifications_Written_For_The_Response_Contain_No_Salary_Or_Terms()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();

        await h.RespondHandler().HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None);

        foreach (var notification in h.Wiring.Notifications.Written)
        {
            var text = notification.Title + " " + notification.Body;
            Assert.DoesNotContain("72000", text);
            Assert.DoesNotContain("72,000", text);
            Assert.DoesNotContain("GBP", text);
        }
    }

    [Fact]
    public async Task Employee_Declines_With_An_Optional_Reason_And_The_Task_Is_Closed()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();

        var result = await h.RespondHandler().HandleAsync(
            h.Respond("Decline", reason: "  Not the right time.  "), h.EmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var saved = await h.ReloadAsync();
        Assert.Equal(OfferResponseStatus.Declined, saved.OfferResponseStatus);
        Assert.Equal("Not the right time.", saved.OfferResponseReason);
        Assert.Single(h.Wiring.Completer.Calls);
    }

    [Fact]
    public async Task Employee_Declines_Without_A_Reason()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();

        var result = await h.RespondHandler().HandleAsync(h.Respond("decline"), h.EmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null((await h.ReloadAsync()).OfferResponseReason);
    }

    [Fact]
    public async Task Reason_Is_Ignored_When_Accepting()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();

        await h.RespondHandler().HandleAsync(h.Respond("Accept", reason: "Thanks"), h.EmployeeId, CancellationToken.None);

        Assert.Null((await h.ReloadAsync()).OfferResponseReason);
    }

    [Fact]
    public async Task Another_Employee_Cannot_Respond_To_The_Offer()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();

        var result = await h.RespondHandler().HandleAsync(h.Respond("Accept"), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, (await h.ReloadAsync()).OfferResponseStatus);
        Assert.Empty(h.Wiring.Completer.Calls);
    }

    [Fact]
    public async Task Response_For_An_Application_In_Another_Company_Is_Not_Found()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();

        var result = await h.RespondHandler().HandleAsync(
            h.Respond("Accept") with { CompanyId = Guid.NewGuid() }, h.EmployeeId, CancellationToken.None);

        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Response_To_An_Application_With_No_Offer_Is_Not_Found()
    {
        var h = await InternalOfferHarness.CreateAsync();

        var result = await h.RespondHandler().HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None);

        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Response_To_A_Superseded_Version_Is_A_Conflict()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();
        await h.OfferAsync(h.ValidOffer() with { OfferedSalary = 75000m });

        var result = await h.RespondHandler().HandleAsync(h.Respond("Accept", version: 1), h.EmployeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, (await h.ReloadAsync()).OfferResponseStatus);
    }

    [Fact]
    public async Task Repeating_The_Same_Decision_Is_Idempotent_And_Does_Not_Duplicate_Effects()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();
        await h.RespondHandler().HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None);
        var versionAfterFirst = (await h.ReloadAsync()).Version;
        var notificationsAfterFirst = h.Wiring.Notifications.Written.Count;
        var auditAfterFirst = h.Audit.Published.OfType<OfferResponseRecordedAuditEvent>().Count();

        var second = await h.RespondHandler().HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value!.WasAlreadyRecorded);
        Assert.Equal(versionAfterFirst, (await h.ReloadAsync()).Version);
        Assert.Equal(notificationsAfterFirst, h.Wiring.Notifications.Written.Count);
        Assert.Equal(auditAfterFirst, h.Audit.Published.OfType<OfferResponseRecordedAuditEvent>().Count());
        Assert.Single(h.Wiring.Completer.Calls);
    }

    [Fact]
    public async Task The_Opposite_Decision_After_A_Response_Is_A_Conflict()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();
        await h.RespondHandler().HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None);

        var result = await h.RespondHandler().HandleAsync(h.Respond("Decline"), h.EmployeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(OfferResponseStatus.Accepted, (await h.ReloadAsync()).OfferResponseStatus);
    }

    [Fact]
    public async Task Response_To_A_Withdrawn_Application_Is_Rejected()
    {
        var h = await InternalOfferHarness.CreateAsync();
        var saved = await h.OfferAsync();
        saved.Withdraw(InternalOfferHarness.Now);
        await h.Db.SaveChangesAsync();

        var result = await h.RespondHandler().HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(OfferResponseStatus.AwaitingResponse, (await h.ReloadAsync()).OfferResponseStatus);
    }

    [Fact]
    public async Task Response_From_An_Employee_Who_Is_No_Longer_Active_Is_Rejected()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();
        h.Wiring.Applicants.Set(FakeEmployeeApplicantReader.Profile(
            h.CompanyId, h.EmployeeId, state: EmployeeApplicantEmploymentState.Former));

        var result = await h.RespondHandler().HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task Employee_Response_Audit_Event_Has_The_Channel_And_No_Salary()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();

        await h.RespondHandler().HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None);

        var audit = Assert.Single(h.Audit.Published.OfType<OfferResponseRecordedAuditEvent>());
        Assert.Equal("Employee", audit.Channel);
        Assert.Equal(h.EmployeeId, audit.PerformedByUserId);
        var serialised = System.Text.Json.JsonSerializer.Serialize(
            new object?[] { ((IAuditEvent)audit).Before, ((IAuditEvent)audit).After, ((IAuditEvent)audit).Metadata });
        Assert.DoesNotContain("72000", serialised);
    }

    [Fact]
    public async Task Recruiter_Recorded_Response_Resolves_The_Employee_Task_Without_Notifying_Recruiters()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();
        var recruiterId = Guid.NewGuid();
        var handler = new RespondToOfferHandler(h.Db, h.Clock, h.Audit, h.Wiring.Effects);

        var result = await handler.HandleAsync(
            new RespondToOfferRequest
            {
                CompanyId = h.CompanyId, VacancyId = h.Vacancy.Id, ApplicationId = h.Application.Id, Status = "Accepted",
            },
            recruiterId, CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var saved = await h.ReloadAsync();
        Assert.Equal(OfferResponseChannel.Recruiter, saved.OfferResponseChannel);
        Assert.Equal(recruiterId, saved.OfferRespondedByUserId);

        var completion = Assert.Single(h.Wiring.Completer.Calls);
        Assert.Equal(h.Application.Id, completion.SourceEntityId);
        Assert.Equal(recruiterId, completion.CompletedBy);
        Assert.DoesNotContain(h.Wiring.Notifications.Written, n => n.Type == NotificationType.InternalOfferResponded);
        Assert.NotNull((await h.Db.InternalOfferTaskEffects.SingleAsync()).ClosedAt);
    }

    [Fact]
    public async Task Recruiter_Withdrawing_The_Offer_Cancels_The_Employee_Task()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();
        var handler = new RespondToOfferHandler(h.Db, h.Clock, h.Audit, h.Wiring.Effects);

        await handler.HandleAsync(
            new RespondToOfferRequest
            {
                CompanyId = h.CompanyId, VacancyId = h.Vacancy.Id, ApplicationId = h.Application.Id, Status = "Withdrawn",
            },
            Guid.NewGuid(), CancellationToken.None);

        Assert.Contains(h.Wiring.Canceller.Calls, c => c.SourceEntityIds.Contains(h.Application.Id) && c.ActionType == TaskActionType.Approve);
        Assert.Empty(h.Wiring.Completer.Calls);
        Assert.NotNull((await h.Db.InternalOfferTaskEffects.SingleAsync()).ClosedAt);
    }

    [Fact]
    public async Task Withdrawing_The_Application_Cancels_The_Employee_Task()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();
        var handler = new WithdrawApplicationHandler(h.Db, h.Clock, h.Audit, h.Wiring.Effects);

        var result = await handler.HandleAsync(
            new WithdrawApplicationRequest { CompanyId = h.CompanyId, VacancyId = h.Vacancy.Id, ApplicationId = h.Application.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Contains(h.Wiring.Canceller.Calls, c => c.SourceEntityIds.Contains(h.Application.Id));
        Assert.NotNull((await h.Db.InternalOfferTaskEffects.SingleAsync()).ClosedAt);
    }

    [Fact]
    public async Task Rejecting_The_Application_Cancels_The_Employee_Task()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();
        var saved = await h.ReloadAsync();
        saved.RecordRejection(h.Stages.Rejected.Id, "No longer needed", InternalOfferHarness.Now);
        await h.Db.SaveChangesAsync();

        await h.Wiring.Effects.RunAllOutstandingAsync(CancellationToken.None);

        Assert.Contains(h.Wiring.Canceller.Calls, c => c.SourceEntityIds.Contains(h.Application.Id));
        Assert.NotNull((await h.Db.InternalOfferTaskEffects.SingleAsync()).ClosedAt);
    }

    [Fact]
    public async Task Reconciliation_Closes_A_Task_Whose_Response_Effect_Failed()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();
        h.Wiring.Resolution.CompletionConfirmed = false;

        await h.RespondHandler().HandleAsync(h.Respond("Accept"), h.EmployeeId, CancellationToken.None);

        var outstanding = await h.Db.InternalOfferTaskEffects.SingleAsync();
        Assert.Null(outstanding.ClosedAt);

        h.Wiring.Resolution.CompletionConfirmed = true;
        await h.Wiring.Effects.RunAllOutstandingAsync(CancellationToken.None);

        Assert.NotNull((await h.Db.InternalOfferTaskEffects.SingleAsync()).ClosedAt);
        Assert.Contains(h.Wiring.Notifications.Written, n => n.Type == NotificationType.InternalOfferResponded);
    }

    [Fact]
    public async Task Notification_Source_Id_Is_Deterministic_Per_Application_And_Version()
    {
        var applicationId = Guid.NewGuid();

        Assert.Equal(
            InternalOfferTaskEffectsService.NotificationSourceId(applicationId, 1),
            InternalOfferTaskEffectsService.NotificationSourceId(applicationId, 1));
        Assert.NotEqual(
            InternalOfferTaskEffectsService.NotificationSourceId(applicationId, 1),
            InternalOfferTaskEffectsService.NotificationSourceId(applicationId, 2));
    }

    [Fact]
    public async Task Recipient_Can_View_The_Complete_Snapshot_And_Respond()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();

        var result = await h.GetHandler().HandleAsync(
            new Features.GetInternalOffer.GetInternalOfferRequest { CompanyId = h.CompanyId, ApplicationId = h.Application.Id },
            h.EmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsOfferRecipient);
        Assert.True(result.Value.CanRespond);
        Assert.Equal("Engineering Manager", result.Value.Terms.JobTitle);
        Assert.Equal(72000m, result.Value.Terms.Salary);
        Assert.Equal("GBP", result.Value.Terms.Currency);
        Assert.Equal("Mo Manager", result.Value.Terms.ProposedManagerName);
        Assert.Equal(5, result.Value.Terms.WorkingDays.Length);
        Assert.Equal(3, result.Value.InternalAppointmentNotices.Count);
        Assert.Contains(result.Value.InternalAppointmentNotices, n => n.Contains("No new employee record", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Another_Employee_Cannot_View_The_Offer()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();

        var result = await h.GetHandler().HandleAsync(
            new Features.GetInternalOffer.GetInternalOfferRequest { CompanyId = h.CompanyId, ApplicationId = h.Application.Id },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Recruitment_Manager_Can_View_But_Not_Respond()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();
        var recruiterId = Guid.NewGuid();
        var authorization = new FakePermissionAuthorizationService().Grant(recruiterId, SystemPermissions.RecruitmentManage);

        var result = await h.GetHandler(authorization).HandleAsync(
            new Features.GetInternalOffer.GetInternalOfferRequest { CompanyId = h.CompanyId, ApplicationId = h.Application.Id },
            recruiterId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsOfferRecipient);
        Assert.False(result.Value.CanRespond);
    }

    [Fact]
    public async Task External_Application_Offers_Are_Not_Visible_Through_The_Self_Service_Endpoint()
    {
        var h = await InternalOfferHarness.CreateAsync(internalApplication: false);
        await h.OfferAsync(h.ValidOffer() with { ProposedManagerId = null, Currency = null });

        var result = await h.GetHandler().HandleAsync(
            new Features.GetInternalOffer.GetInternalOfferRequest { CompanyId = h.CompanyId, ApplicationId = h.Application.Id },
            h.EmployeeId, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Offer_That_Is_No_Longer_Awaiting_Cannot_Be_Responded_To_From_The_View()
    {
        var h = await InternalOfferHarness.CreateAsync();
        await h.OfferAsync();
        await h.RespondHandler().HandleAsync(h.Respond("Decline"), h.EmployeeId, CancellationToken.None);

        var result = await h.GetHandler().HandleAsync(
            new Features.GetInternalOffer.GetInternalOfferRequest { CompanyId = h.CompanyId, ApplicationId = h.Application.Id },
            h.EmployeeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.CanRespond);
        Assert.NotNull(result.Value.CannotRespondReason);
    }
}
