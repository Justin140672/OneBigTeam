using HR.Modules.Leave.Domain;
using HR.Modules.Leave.Features.SubmitLeaveRequestDraft;
using HR.Modules.Leave.Persistence;
using HR.Modules.Leave.Services;
using HR.Modules.Leave.Tests.Infrastructure;
using HR.Modules.Employees.Contracts;
using HR.Infrastructure.Abstractions;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Leave.Tests;

public class SubmitLeaveRequestDraftHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 6, 12, 9, 0, 0, DateTimeKind.Utc);

    private static LeaveDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<LeaveDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new LeaveDbContext(options);
    }

    private static SubmitLeaveRequestDraftHandler BuildHandler(
        LeaveDbContext context,
        HR.SharedKernel.IIntegrationEventPublisher? publisher = null,
        HR.SharedKernel.IAuditEventPublisher? auditPublisher = null,
        LeaveApprovalEffectsService? approvalEffects = null) =>
        new(context, new FakeClock(FixedUtcNow), new FakeWorkingPatternProvider(),
            new FakeCompanyLeaveSettingsReader(), new FakePublicHolidayReader(),
            publisher ?? new NoOpIntegrationEventPublisher(),
            auditPublisher ?? new NoOpAuditEventPublisher(),
            approvalEffects ?? new LeaveApprovalEffectsService(
                context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(),
                new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)),
            new LeaveWarningCalculator(new FakePublicHolidayReader()));

    private static SubmitLeaveRequestDraftRequest SubmitRequest(Guid companyId, Guid employeeId, Guid leaveRequestId) => new()
    {
        CompanyId = companyId,
        EmployeeId = employeeId,
        LeaveRequestId = leaveRequestId
    };

    // 2026-08-03 = Monday, 2026-08-07 = Friday
    private static async Task<(LeaveType LeaveType, LeavePolicy Policy, LeaveRequest Draft)> SeedDraftWithPolicyAsync(
        LeaveDbContext context, Guid companyId, Guid employeeId, bool requiresApproval,
        decimal entitlementDays = 25, bool allowNegativeBalance = false)
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", (int)entitlementDays,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", null, 5,
            allowNegativeBalance, false, now, requiresApproval);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            DateOnly.FromDateTime(FixedUtcNow), now);
        var balance = LeaveBalance.Create(Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            FixedUtcNow.Year, entitlementDays, new DateOnly(FixedUtcNow.Year, 1, 1), now);

        var draft = LeaveRequest.CreateDraft(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, null,
            new DateOnly(2026, 8, 3), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 7), LeaveDayPart.FullDay,
            5m, "Family holiday", now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveBalances.Add(balance);
        context.LeaveRequests.Add(draft);
        await context.SaveChangesAsync();

        return (leaveType, policy, draft);
    }

    [Fact]
    public async Task HandleAsync_Returns_ExcludedPublicHolidays_Consistent_With_PreviewAndSubmit()
    {
        // LEAVE-08: submitting a draft must surface the same public-holiday-in-range warning
        // PreviewLeaveRequestHandler/SubmitLeaveRequestHandler return, via the same shared
        // LeaveWarningCalculator.
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (_, _, draft) = await SeedDraftWithPolicyAsync(context, companyId, employeeId, requiresApproval: true);

        // 2026-08-05 (Wednesday) falls inside the draft's 2026-08-03..2026-08-07 range.
        var holidayReader = new FakePublicHolidayReader([new DateOnly(2026, 8, 5)], "Summer Bank Holiday");

        var handler = new SubmitLeaveRequestDraftHandler(context, new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(),
            new FakeCompanyLeaveSettingsReader(CompanyLeaveSettings.Default with { ExcludePublicHolidaysFromLeave = true }),
            holidayReader,
            new NoOpIntegrationEventPublisher(), new NoOpAuditEventPublisher(),
            new LeaveApprovalEffectsService(context, new NoOpNotificationWriter(), new NoOpIntegrationEventPublisher(), new FakeCompanyLeaveSettingsReader(), new NoOpAuditEventPublisher(), new ToilLedgerService(context)),
            new LeaveWarningCalculator(holidayReader));

        var result = await handler.HandleAsync(SubmitRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var excluded = Assert.Single(result.Value!.ExcludedPublicHolidays);
        Assert.Equal(new DateOnly(2026, 8, 5), excluded.Date);
        Assert.Equal("Summer Bank Holiday", excluded.Name);
    }

    [Fact]
    public async Task HandleAsync_RequiresApproval_True_Transitions_Draft_To_Pending()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (_, _, draft) = await SeedDraftWithPolicyAsync(context, companyId, employeeId, requiresApproval: true);

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(SubmitRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Pending", result.Value!.Status);

        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Pending, saved.Status);
    }

    [Fact]
    public async Task HandleAsync_RequiresApproval_True_Publishes_LeaveRequestedIntegrationEvent_And_Does_Not_Deduct_Balance()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (_, _, draft) = await SeedDraftWithPolicyAsync(context, companyId, employeeId, requiresApproval: true);

        var publisher = new CapturingIntegrationEventPublisher();
        var handler = BuildHandler(context, publisher: publisher);
        var result = await handler.HandleAsync(SubmitRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var evt = Assert.Single(publisher.Published);
        Assert.IsType<LeaveRequestedIntegrationEvent>(evt);

        var balance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(0m, balance.UsedDays);
    }

    [Fact]
    public async Task HandleAsync_RequiresApproval_False_Transitions_Draft_To_Approved_Directly()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (_, _, draft) = await SeedDraftWithPolicyAsync(context, companyId, employeeId, requiresApproval: false);

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(SubmitRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Approved", result.Value!.Status);

        var saved = await context.LeaveRequests.SingleAsync();
        Assert.Equal(LeaveRequestStatus.Approved, saved.Status);
        Assert.Equal(employeeId, saved.ReviewedByEmployeeId);
    }

    [Fact]
    public async Task HandleAsync_RequiresApproval_False_Deducts_Balance_Atomically()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (_, _, draft) = await SeedDraftWithPolicyAsync(context, companyId, employeeId, requiresApproval: false);

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(SubmitRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);

        var balance = await context.LeaveBalances.SingleAsync();
        Assert.Equal(5m, balance.UsedDays);
        Assert.Equal(20m, balance.RemainingDays);
    }

    [Fact]
    public async Task HandleAsync_RequiresApproval_False_Publishes_ApprovedAuditEvent_IntegrationEvent_And_Notification()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (_, _, draft) = await SeedDraftWithPolicyAsync(context, companyId, employeeId, requiresApproval: false);

        var auditPublisher = new CapturingAuditEventPublisher();
        var integrationPublisher = new CapturingIntegrationEventPublisher();
        var notif = new FakeNotificationWriter();
        var approvalEffects = new LeaveApprovalEffectsService(
            context, notif, integrationPublisher, new FakeCompanyLeaveSettingsReader(),
            auditPublisher, new ToilLedgerService(context));

        var handler = BuildHandler(context, approvalEffects: approvalEffects);
        var result = await handler.HandleAsync(SubmitRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);

        // LeaveSubmittedAuditEvent is always published (by the draft submit handler itself) plus
        // LeaveApprovedAuditEvent from the approval-effects fan-out.
        Assert.Contains(auditPublisher.Published, e => e is LeaveApprovedAuditEvent);

        Assert.Single(integrationPublisher.Published);
        Assert.IsType<LeaveApprovedIntegrationEvent>(Assert.Single(integrationPublisher.Published));

        var written = Assert.Single(notif.Written);
        Assert.Equal(NotificationType.LeaveApproved, written.Type);
    }

    [Fact]
    public async Task HandleAsync_RequiresApproval_False_Does_Not_Publish_LeaveRequestedIntegrationEvent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (_, _, draft) = await SeedDraftWithPolicyAsync(context, companyId, employeeId, requiresApproval: false);

        var publisher = new CapturingIntegrationEventPublisher();
        var handler = BuildHandler(context, publisher: publisher);
        var result = await handler.HandleAsync(SubmitRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(publisher.Published, e => e is LeaveRequestedIntegrationEvent);
    }

    [Fact]
    public async Task HandleAsync_Always_Publishes_LeaveSubmittedAuditEvent()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var (_, _, draft) = await SeedDraftWithPolicyAsync(context, companyId, employeeId, requiresApproval: true);

        var auditPublisher = new CapturingAuditEventPublisher();
        var handler = BuildHandler(context, auditPublisher: auditPublisher);
        var result = await handler.HandleAsync(SubmitRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(auditPublisher.Published, e => e is LeaveSubmittedAuditEvent);
    }

    [Theory]
    [InlineData((int)LeaveRequestStatus.Pending)]
    [InlineData((int)LeaveRequestStatus.Approved)]
    [InlineData((int)LeaveRequestStatus.Rejected)]
    [InlineData((int)LeaveRequestStatus.Cancelled)]
    public async Task HandleAsync_Returns_Validation_Error_When_Request_Is_Not_Draft(int statusValue)
    {
        var status = (LeaveRequestStatus)statusValue;
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var (_, _, draft) = await SeedDraftWithPolicyAsync(context, companyId, employeeId, requiresApproval: true);

        switch (status)
        {
            case LeaveRequestStatus.Pending:
                draft.MarkSubmittedPending(now);
                break;
            case LeaveRequestStatus.Approved:
                draft.Approve(Guid.NewGuid(), now);
                break;
            case LeaveRequestStatus.Rejected:
                draft.MarkSubmittedPending(now);
                draft.Reject(Guid.NewGuid(), now);
                break;
            case LeaveRequestStatus.Cancelled:
                draft.MarkSubmittedPending(now);
                draft.Cancel(now);
                break;
        }
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(SubmitRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Draft_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(
            SubmitRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Cross_Year_Draft_At_Submit_Time()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var leaveType = LeaveType.Create(Guid.NewGuid(), companyId, "Annual Leave", "ANNUAL", 25,
            AccrualMethod.None, LeaveTypeBehaviour.Standard, now);
        var policy = LeavePolicy.Create(Guid.NewGuid(), companyId, "Standard Policy", null, 5, false, false, now, true);
        var assignment = EmployeeLeavePolicyAssignment.Create(Guid.NewGuid(), companyId, employeeId, policy.Id,
            DateOnly.FromDateTime(FixedUtcNow), now);
        var draft = LeaveRequest.CreateDraft(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, null,
            new DateOnly(2026, 12, 28), LeaveDayPart.FullDay,
            new DateOnly(2027, 1, 4), LeaveDayPart.FullDay,
            5m, "Cross year", now);

        context.LeaveTypes.Add(leaveType);
        context.LeavePolicies.Add(policy);
        context.EmployeeLeavePolicyAssignments.Add(assignment);
        context.LeaveRequests.Add(draft);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(SubmitRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Rejects_Insufficient_Balance_At_Submit_Time()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        // 2 days entitlement, requesting Mon-Fri (5 days), no negative balance allowed
        var (_, _, draft) = await SeedDraftWithPolicyAsync(
            context, companyId, employeeId, requiresApproval: true, entitlementDays: 2, allowNegativeBalance: false);

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(SubmitRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Populates_Conflicts_For_Overlapping_NonDraft_Request()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var (leaveType, policy, draft) = await SeedDraftWithPolicyAsync(context, companyId, employeeId, requiresApproval: true);

        var existing = LeaveRequest.Create(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, policy.Id,
            new DateOnly(2026, 8, 5), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 6), LeaveDayPart.FullDay,
            2m, "Existing", now);
        context.LeaveRequests.Add(existing);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(SubmitRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Conflicts);
        Assert.Equal(existing.Id, result.Value.Conflicts[0].LeaveRequestId);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Flag_Another_Draft_As_Conflict()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);

        var (leaveType, _, draft) = await SeedDraftWithPolicyAsync(context, companyId, employeeId, requiresApproval: true);

        var otherDraft = LeaveRequest.CreateDraft(
            Guid.NewGuid(), companyId, employeeId, leaveType.Id, null,
            new DateOnly(2026, 8, 5), LeaveDayPart.FullDay,
            new DateOnly(2026, 8, 6), LeaveDayPart.FullDay,
            2m, "Other draft", now);
        context.LeaveRequests.Add(otherDraft);
        await context.SaveChangesAsync();

        var handler = BuildHandler(context);
        var result = await handler.HandleAsync(SubmitRequest(companyId, employeeId, draft.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Conflicts);
    }
}

public class SubmitLeaveRequestDraftValidatorTests
{
    private readonly SubmitLeaveRequestDraftValidator _validator = new();

    private static SubmitLeaveRequestDraftRequest ValidRequest() => new()
    {
        CompanyId = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        LeaveRequestId = Guid.NewGuid()
    };

    [Fact]
    public void Validate_Succeeds_For_Valid_Request()
    {
        Assert.True(_validator.Validate(ValidRequest()).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_CompanyId_Is_Empty()
    {
        Assert.False(_validator.Validate(ValidRequest() with { CompanyId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_EmployeeId_Is_Empty()
    {
        Assert.False(_validator.Validate(ValidRequest() with { EmployeeId = Guid.Empty }).IsValid);
    }

    [Fact]
    public void Validate_Fails_When_LeaveRequestId_Is_Empty()
    {
        Assert.False(_validator.Validate(ValidRequest() with { LeaveRequestId = Guid.Empty }).IsValid);
    }
}
