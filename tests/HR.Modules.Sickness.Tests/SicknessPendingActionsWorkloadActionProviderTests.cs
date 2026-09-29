using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

public class SicknessPendingActionsWorkloadActionProviderTests
{
    private static SicknessDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new SicknessDbContext(options);
    }

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static SicknessRecord CreateRecord(Guid companyId, Guid employeeId, DateOnly startDate) =>
        SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            startDate, SicknessDayPart.FullDay, null, null, null, null,
            SicknessEvidenceStatus.NotRequired, DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetActionsAsync_HrCaller_Sees_Pending_And_Overdue_ReturnToWorkReviews_CompanyWide()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();

        var recordA = CreateRecord(companyId, employeeA, new DateOnly(2026, 7, 1));
        var recordB = CreateRecord(companyId, employeeB, new DateOnly(2026, 7, 5));
        context.SicknessRecords.AddRange(recordA, recordB);

        var reviewA = ReturnToWorkReview.Create(
            Guid.NewGuid(), companyId, recordA.Id, employeeA, new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow);
        var reviewB = ReturnToWorkReview.Create(
            Guid.NewGuid(), companyId, recordB.Id, employeeB, new DateOnly(2026, 7, 12), DateTimeOffset.UtcNow);
        context.ReturnToWorkReviews.AddRange(reviewA, reviewB);
        await context.SaveChangesAsync();

        var provider = new SicknessPendingActionsWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(), new FakeDirectReportsReader(), new FakeCurrentUser(Guid.NewGuid()));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), WorkloadScope.Hr, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.Equal("Complete Return to Work Review", a.ActionType));
        Assert.All(result, a => Assert.False(a.IsOwnerActionable));
        Assert.All(result, a => Assert.Equal("Owned by the employee's manager", a.OwnerLabel));
    }

    [Fact]
    public async Task GetActionsAsync_ManagerCaller_Returns_Empty_Sickness_Is_HrOnly()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var record = CreateRecord(companyId, employeeId, new DateOnly(2026, 7, 1));
        context.SicknessRecords.Add(record);
        context.ReturnToWorkReviews.Add(
            ReturnToWorkReview.Create(Guid.NewGuid(), companyId, record.Id, employeeId, new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var provider = new SicknessPendingActionsWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService(),
            new FakeOpenTaskBySourceEntityReader(), new FakeDirectReportsReader([employeeId]), new FakeCurrentUser(Guid.NewGuid()));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), WorkloadScope.Hr, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActionsAsync_CallerWithNoRole_Returns_Empty_Not_Throws()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var record = CreateRecord(companyId, employeeId, new DateOnly(2026, 7, 1));
        context.SicknessRecords.Add(record);
        await context.SaveChangesAsync();

        var provider = new SicknessPendingActionsWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService(),
            new FakeOpenTaskBySourceEntityReader(), new FakeDirectReportsReader(), new FakeCurrentUser(Guid.NewGuid()));

        var result = await provider.GetActionsAsync(companyId, new ClaimsPrincipal(new ClaimsIdentity()), WorkloadScope.Hr, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActionsAsync_HrCaller_Requesting_ManagerScope_With_No_Team_Returns_Empty()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var callerId = Guid.NewGuid();

        var record = CreateRecord(companyId, employeeId, new DateOnly(2026, 7, 1));
        context.SicknessRecords.Add(record);
        context.ReturnToWorkReviews.Add(
            ReturnToWorkReview.Create(Guid.NewGuid(), companyId, record.Id, employeeId, new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var provider = new SicknessPendingActionsWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(), new FakeDirectReportsReader([]), new FakeCurrentUser(callerId));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerId), WorkloadScope.Manager, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActionsAsync_ManagerScope_CallerWithNoResolvedEmployeeId_Returns_Empty()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var record = CreateRecord(companyId, employeeId, new DateOnly(2026, 7, 1));
        context.SicknessRecords.Add(record);
        context.ReturnToWorkReviews.Add(
            ReturnToWorkReview.Create(Guid.NewGuid(), companyId, record.Id, employeeId, new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var provider = new SicknessPendingActionsWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService(),
            new FakeOpenTaskBySourceEntityReader(), new FakeDirectReportsReader([employeeId]), new FakeCurrentUser(null));

        var result = await provider.GetActionsAsync(companyId, new ClaimsPrincipal(new ClaimsIdentity()), WorkloadScope.Manager, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetActionsAsync_ManagerScope_Sees_Only_ReturnToWorkReviews_For_Own_ReportingSubtree_As_OwnerActionable()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var directReportId = Guid.NewGuid();
        var outsideEmployeeId = Guid.NewGuid();

        var recordIn = CreateRecord(companyId, directReportId, new DateOnly(2026, 7, 1));
        var recordOut = CreateRecord(companyId, outsideEmployeeId, new DateOnly(2026, 7, 1));
        context.SicknessRecords.AddRange(recordIn, recordOut);

        var reviewIn = ReturnToWorkReview.Create(
            Guid.NewGuid(), companyId, recordIn.Id, directReportId, new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow);
        var reviewOut = ReturnToWorkReview.Create(
            Guid.NewGuid(), companyId, recordOut.Id, outsideEmployeeId, new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow);
        context.ReturnToWorkReviews.AddRange(reviewIn, reviewOut);

        context.SicknessEvidenceRequests.Add(
            SicknessEvidenceRequest.Create(Guid.NewGuid(), companyId, recordIn.Id, Guid.NewGuid(),
                new DateOnly(2026, 7, 20), null, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var provider = new SicknessPendingActionsWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService(),
            new FakeOpenTaskBySourceEntityReader(), new FakeDirectReportsReader([directReportId]), new FakeCurrentUser(managerId));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(managerId), WorkloadScope.Manager, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(directReportId, action.EmployeeId);
        Assert.Equal("Complete Return to Work Review", action.ActionType);
        Assert.True(action.IsOwnerActionable);
        Assert.Null(action.OwnerLabel);
    }

    [Fact]
    public async Task GetActionsAsync_Maps_EvidenceRequest_ActionType_Category_DueDate_No_EmployeeProfileFallback()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dueDate = new DateOnly(2026, 7, 20);
        var evidenceRequestId = Guid.NewGuid();

        var record = CreateRecord(companyId, employeeId, new DateOnly(2026, 7, 1));
        context.SicknessRecords.Add(record);
        context.SicknessEvidenceRequests.Add(
            SicknessEvidenceRequest.Create(evidenceRequestId, companyId, record.Id, Guid.NewGuid(), dueDate, null, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var provider = new SicknessPendingActionsWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(), new FakeDirectReportsReader(), new FakeCurrentUser(Guid.NewGuid()));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), WorkloadScope.Hr, CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Follow Up Sickness Evidence Request", action.ActionType);
        Assert.Equal("Pending Sickness Actions", action.ActionCategory);
        Assert.Equal(dueDate, action.DueDate);
        Assert.Equal("", action.DeepLinkUrl);
        Assert.False(action.IsOwnerActionable);
        Assert.Equal("Owned by the employee", action.OwnerLabel);
    }

    [Fact]
    public async Task GetActionsAsync_Resolves_Exact_Linked_Task_For_ReturnToWorkReview_And_EvidenceRequest()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var record = CreateRecord(companyId, employeeId, new DateOnly(2026, 7, 1));
        context.SicknessRecords.Add(record);

        var review = ReturnToWorkReview.Create(
            Guid.NewGuid(), companyId, record.Id, employeeId, new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow);
        context.ReturnToWorkReviews.Add(review);

        var evidenceRequestId = Guid.NewGuid();
        context.SicknessEvidenceRequests.Add(
            SicknessEvidenceRequest.Create(evidenceRequestId, companyId, record.Id, Guid.NewGuid(),
                new DateOnly(2026, 7, 20), null, DateTimeOffset.UtcNow));
        await context.SaveChangesAsync();

        var reviewTaskId = Guid.NewGuid();
        var evidenceTaskId = Guid.NewGuid();
        var provider = new SicknessPendingActionsWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(new Dictionary<Guid, Guid>
            {
                [review.Id] = reviewTaskId,
                [evidenceRequestId] = evidenceTaskId,
            }), new FakeDirectReportsReader(), new FakeCurrentUser(Guid.NewGuid()));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), WorkloadScope.Hr, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, a => a.ActionType == "Complete Return to Work Review" && a.TaskId == reviewTaskId);
        Assert.Contains(result, a => a.ActionType == "Follow Up Sickness Evidence Request" && a.TaskId == evidenceTaskId);
    }

    [Fact]
    public async Task GetActionsAsync_Multiple_ReturnToWorkReviews_With_Identical_ActionType_Each_Resolve_Their_Own_Distinct_TaskId()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();

        var recordA = CreateRecord(companyId, employeeA, new DateOnly(2026, 7, 1));
        var recordB = CreateRecord(companyId, employeeB, new DateOnly(2026, 7, 1));
        context.SicknessRecords.AddRange(recordA, recordB);

        var reviewA = ReturnToWorkReview.Create(
            Guid.NewGuid(), companyId, recordA.Id, employeeA, new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow);
        var reviewB = ReturnToWorkReview.Create(
            Guid.NewGuid(), companyId, recordB.Id, employeeB, new DateOnly(2026, 7, 10), DateTimeOffset.UtcNow);
        context.ReturnToWorkReviews.AddRange(reviewA, reviewB);
        await context.SaveChangesAsync();

        var taskIdA = Guid.NewGuid();
        var taskIdB = Guid.NewGuid();
        var provider = new SicknessPendingActionsWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService("reporting:view-hr"),
            new FakeOpenTaskBySourceEntityReader(new Dictionary<Guid, Guid>
            {
                [reviewA.Id] = taskIdA,
                [reviewB.Id] = taskIdB,
            }), new FakeDirectReportsReader(), new FakeCurrentUser(Guid.NewGuid()));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), WorkloadScope.Hr, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, a => a.EmployeeId == employeeA && a.TaskId == taskIdA);
        Assert.Contains(result, a => a.EmployeeId == employeeB && a.TaskId == taskIdB);
        Assert.NotEqual(
            result.Single(a => a.EmployeeId == employeeA).TaskId,
            result.Single(a => a.EmployeeId == employeeB).TaskId);
    }
}
