using System.Security.Claims;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Services;
using HR.Modules.Sickness.Tests.Infrastructure;
using HR.Modules.Tasks.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

/// <summary>
/// OBT-721 workload action provider tests for pending sickness administration — HR-only category
/// (see xmldoc on the provider). Mirrors GetProbationReportHandlerTests-style coverage, adapted for
/// an HR-only (no Manager row-scoping) provider.
/// </summary>
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
            new FakeOpenTaskBySourceEntityReader());

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.All(result, a => Assert.Equal("Complete Return to Work Review", a.ActionType));
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

        // Manager but not HR — this category is HR-only, so even a Manager with direct reports
        // must get nothing back.
        var provider = new SicknessPendingActionsWorkloadActionProvider(
            context, new FakeEmployeeDepartmentReader(), new FakeAuthorizationService(),
            new FakeOpenTaskBySourceEntityReader());

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

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
            new FakeOpenTaskBySourceEntityReader());

        var result = await provider.GetActionsAsync(companyId, new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None);

        Assert.Empty(result);
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
            new FakeOpenTaskBySourceEntityReader());

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Follow Up Sickness Evidence Request", action.ActionType);
        Assert.Equal("Pending Sickness Actions", action.ActionCategory);
        Assert.Equal(dueDate, action.DueDate);
        // No employee-profile fallback: entirely task-backed category.
        Assert.Equal("", action.DeepLinkUrl);
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
            }));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, a => a.ActionType == "Complete Return to Work Review" && a.TaskId == reviewTaskId);
        Assert.Contains(result, a => a.ActionType == "Follow Up Sickness Evidence Request" && a.TaskId == evidenceTaskId);
    }

    [Fact]
    public async Task GetActionsAsync_Multiple_ReturnToWorkReviews_With_Identical_ActionType_Each_Resolve_Their_Own_Distinct_TaskId()
    {
        // Two different employees' return-to-work reviews share the exact same ActionType/Category
        // text — the linked task must be resolved by the review's own id, never by
        // title/employee matching.
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
            }));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, a => a.EmployeeId == employeeA && a.TaskId == taskIdA);
        Assert.Contains(result, a => a.EmployeeId == employeeB && a.TaskId == taskIdB);
        Assert.NotEqual(
            result.Single(a => a.EmployeeId == employeeA).TaskId,
            result.Single(a => a.EmployeeId == employeeB).TaskId);
    }
}
