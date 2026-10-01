using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class SicknessWorkloadActionabilityTests
{
    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static SicknessRecord CreateRecord(Guid companyId, Guid employeeId) =>
        SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 7, 1), SicknessDayPart.FullDay, null, null, null, null,
            SicknessEvidenceStatus.NotRequired, DateTimeOffset.UtcNow);

    private static SicknessPendingActionsWorkloadActionProvider HrProvider(
        SicknessDbContext context, Guid callerId, FakeOpenTaskBySourceEntityReader? taskReader = null) =>
        new(context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            taskReader ?? new FakeOpenTaskBySourceEntityReader(), new FakeDirectReportsReader(), new FakeCurrentUser(callerId));

    [Fact]
    public async Task HrScope_EvidenceRequest_IsEmployeeOwned_VisibilityOnly_WithReason()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var record = CreateRecord(companyId, Guid.NewGuid());
        context.SicknessRecords.Add(record);
        context.SicknessEvidenceRequests.Add(SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, record.Id, Guid.NewGuid(), new DateOnly(2026, 7, 20), null, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var callerId = Guid.NewGuid();
        var action = Assert.Single(await HrProvider(context, callerId)
            .GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None));

        Assert.Equal(WorkloadActionability.VisibilityOnly, action.Actionability);
        Assert.Equal("Owned by the employee", action.OwnerLabel);
        Assert.Contains("employee is responsible", action.VisibilityReason);
    }

    [Fact]
    public async Task HrScope_EvidenceRequestWhoseSicknessRecordIsMissing_IsUnavailable_NotASilentDeadEnd()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.SicknessEvidenceRequests.Add(SicknessEvidenceRequest.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 20), null, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var callerId = Guid.NewGuid();
        var action = Assert.Single(await HrProvider(context, callerId)
            .GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None));

        Assert.Equal(WorkloadActionability.Unavailable, action.Actionability);
        Assert.Contains("administrator investigation", action.VisibilityReason);
    }

    [Fact]
    public async Task HrScope_ReviewWhoseSicknessRecordIsMissing_IsUnavailable()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        context.ReturnToWorkReviews.Add(ReturnToWorkReview.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var callerId = Guid.NewGuid();
        var action = Assert.Single(await HrProvider(context, callerId)
            .GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None));

        Assert.Equal(WorkloadActionability.Unavailable, action.Actionability);
    }

    [Fact]
    public async Task HrScope_CompletedAndCancelledReviewsAndFulfilledEvidence_AreNotInEitherQueue()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var record = CreateRecord(companyId, employeeId);
        context.SicknessRecords.Add(record);

        var cancelled = ReturnToWorkReview.Create(
            Guid.NewGuid(), companyId, record.Id, employeeId, new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow);
        cancelled.Cancel(DateTimeOffset.UtcNow);
        context.ReturnToWorkReviews.Add(cancelled);
        await context.SaveChangesAsync();

        var callerId = Guid.NewGuid();
        var result = await HrProvider(context, callerId)
            .GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HrScope_ReviewAssignedToTheCaller_WhoIsAlsoTheManager_CanAct()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var record = CreateRecord(companyId, employeeId);
        context.SicknessRecords.Add(record);
        var review = ReturnToWorkReview.Create(
            Guid.NewGuid(), companyId, record.Id, employeeId, new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow);
        context.ReturnToWorkReviews.Add(review);
        await context.SaveChangesAsync();

        var callerId = Guid.NewGuid();
        var linkedTaskId = Guid.NewGuid();
        var taskReader = new FakeOpenTaskBySourceEntityReader(
            new Dictionary<Guid, Guid> { [review.Id] = linkedTaskId },
            new Dictionary<Guid, Guid?> { [linkedTaskId] = callerId });

        var action = Assert.Single(await HrProvider(context, callerId, taskReader)
            .GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None));

        Assert.Equal(WorkloadActionability.CanAct, action.Actionability);
        Assert.Equal(linkedTaskId, action.TaskId);
    }

    [Fact]
    public async Task HrScope_ReviewOwnedByAnotherManager_IsVisibilityOnly()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var record = CreateRecord(companyId, employeeId);
        context.SicknessRecords.Add(record);
        var review = ReturnToWorkReview.Create(
            Guid.NewGuid(), companyId, record.Id, employeeId, new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow);
        context.ReturnToWorkReviews.Add(review);
        await context.SaveChangesAsync();

        var linkedTaskId = Guid.NewGuid();
        var taskReader = new FakeOpenTaskBySourceEntityReader(
            new Dictionary<Guid, Guid> { [review.Id] = linkedTaskId },
            new Dictionary<Guid, Guid?> { [linkedTaskId] = Guid.NewGuid() });

        var callerId = Guid.NewGuid();
        var action = Assert.Single(await HrProvider(context, callerId, taskReader)
            .GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Hr, CancellationToken.None));

        Assert.Equal(WorkloadActionability.VisibilityOnly, action.Actionability);
    }
}
