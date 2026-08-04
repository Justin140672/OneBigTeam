using System.Security.Claims;
using HR.Modules.Probation.Domain;
using HR.Modules.Probation.Persistence;
using HR.Modules.Probation.Services;
using HR.Modules.Probation.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Probation.Tests;

/// <summary>
/// OBT-721 workload action provider tests for probation reviews due/overdue — mirrors
/// GetProbationReportHandlerTests row-scoping coverage (HR company-wide, Manager scoped to direct
/// reports, Manager with no direct reports empty, unrecognised caller empty), plus the due-vs-overdue
/// split that is unique to ProbationReviewWorkloadActions.GetAsync.
/// </summary>
public class ProbationReviewsDueWorkloadActionProviderTests
{
    private static readonly DateOnly Today = new(2026, 7, 29);

    private static ProbationDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<ProbationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ProbationDbContext(options);
    }

    private static ClaimsPrincipal CallerWithSub(Guid employeeId) =>
        new(new ClaimsIdentity([new Claim("sub", employeeId.ToString())]));

    private static (ProbationRecord Record, ProbationReview Review) SeedReview(
        ProbationDbContext context, Guid companyId, Guid employeeId, DateOnly dueDate)
    {
        var record = ProbationRecord.Create(
            Guid.NewGuid(), companyId, employeeId, Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 10, 1), null, DateTimeOffset.UtcNow);
        var review = ProbationReview.Create(
            Guid.NewGuid(), companyId, record.Id, ProbationReviewType.ManagerCheckIn, dueDate, DateTimeOffset.UtcNow);

        context.ProbationRecords.Add(record);
        context.ProbationReviews.Add(review);
        return (record, review);
    }

    // ── ProbationReviewsDueWorkloadActionProvider ───────────────────────────────

    [Fact]
    public async Task DueProvider_HrCaller_Sees_All_Due_Reviews_CompanyWide_Excludes_Overdue()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        SeedReview(context, companyId, Guid.NewGuid(), Today.AddDays(5));  // due
        SeedReview(context, companyId, Guid.NewGuid(), Today.AddDays(-2)); // overdue
        await context.SaveChangesAsync();

        var provider = new ProbationReviewsDueWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeClock(Today.ToDateTime(TimeOnly.MinValue)));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Due", action.Status);
        Assert.Equal("Probation Reviews Due", action.ActionCategory);
    }

    [Fact]
    public async Task DueProvider_ManagerCaller_Is_Scoped_To_DirectReports_Only()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var directReportId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();
        var callerId = Guid.NewGuid();

        SeedReview(context, companyId, directReportId, Today.AddDays(5));
        SeedReview(context, companyId, otherEmployeeId, Today.AddDays(5));
        await context.SaveChangesAsync();

        var provider = new ProbationReviewsDueWorkloadActionProvider(
            context, new FakeDirectReportsReader([directReportId]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-probation"), new FakeClock(Today.ToDateTime(TimeOnly.MinValue)));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(callerId), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal(directReportId, action.EmployeeId);
    }

    [Fact]
    public async Task DueProvider_ManagerWithNoDirectReports_Returns_Empty()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        SeedReview(context, companyId, Guid.NewGuid(), Today.AddDays(5));
        await context.SaveChangesAsync();

        var provider = new ProbationReviewsDueWorkloadActionProvider(
            context, new FakeDirectReportsReader([]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-probation"), new FakeClock(Today.ToDateTime(TimeOnly.MinValue)));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DueProvider_CallerWithNoRecognisedRole_Returns_Empty_Not_Throws()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        SeedReview(context, companyId, Guid.NewGuid(), Today.AddDays(5));
        await context.SaveChangesAsync();

        var provider = new ProbationReviewsDueWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(), new FakeClock(Today.ToDateTime(TimeOnly.MinValue)));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DueProvider_Maps_ActionType_DeepLink_And_DueDate()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var dueDate = Today.AddDays(5);
        SeedReview(context, companyId, employeeId, dueDate);
        await context.SaveChangesAsync();

        var provider = new ProbationReviewsDueWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeClock(Today.ToDateTime(TimeOnly.MinValue)));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Complete ManagerCheckIn Probation Review", action.ActionType);
        Assert.Equal(dueDate, action.DueDate);
        Assert.Equal($"/companies/{companyId}/employees/{employeeId}/view", action.DeepLinkUrl);
    }

    // ── OverdueProbationReviewsWorkloadActionProvider ───────────────────────────

    [Fact]
    public async Task OverdueProvider_Excludes_Reviews_That_Are_Due_But_Not_Yet_Overdue()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        SeedReview(context, companyId, Guid.NewGuid(), Today.AddDays(5));  // due, not overdue
        SeedReview(context, companyId, Guid.NewGuid(), Today.AddDays(-3)); // overdue
        await context.SaveChangesAsync();

        var provider = new OverdueProbationReviewsWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-hr"), new FakeClock(Today.ToDateTime(TimeOnly.MinValue)));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        var action = Assert.Single(result);
        Assert.Equal("Overdue", action.Status);
        Assert.Equal("Overdue Probation Reviews", action.ActionCategory);
        Assert.True(action.DueDate < Today);
    }

    [Fact]
    public async Task OverdueProvider_ManagerWithNoDirectReports_Returns_Empty()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        SeedReview(context, companyId, Guid.NewGuid(), Today.AddDays(-3));
        await context.SaveChangesAsync();

        var provider = new OverdueProbationReviewsWorkloadActionProvider(
            context, new FakeDirectReportsReader([]), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService("reporting:view-probation"), new FakeClock(Today.ToDateTime(TimeOnly.MinValue)));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task OverdueProvider_CallerWithNoRecognisedRole_Returns_Empty_Not_Throws()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        SeedReview(context, companyId, Guid.NewGuid(), Today.AddDays(-3));
        await context.SaveChangesAsync();

        var provider = new OverdueProbationReviewsWorkloadActionProvider(
            context, new FakeDirectReportsReader(), new FakeEmployeeDepartmentReader(),
            new FakeAuthorizationService(), new FakeClock(Today.ToDateTime(TimeOnly.MinValue)));

        var result = await provider.GetActionsAsync(companyId, CallerWithSub(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result);
    }
}
