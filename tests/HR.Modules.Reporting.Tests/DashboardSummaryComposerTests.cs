using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.DashboardSummaries;
using HR.Modules.Reporting.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Reporting.Tests;

/// <summary>
/// DSH-06 stage 1: the shared cross-module dashboard summary composer behind the HR and Manager
/// dashboard summary endpoints. Mirrors GetWorkloadActionsHandlerTests' provider-fan-out fakes — the
/// composer never re-derives authorization, so fixed/throwing fake providers are a faithful stand-in.
/// </summary>
public class DashboardSummaryComposerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);

    private const string LeaveCategory = "Pending Leave Approvals";
    private const string TasksCategory = "Manager Tasks Overdue";

    private static ClaimsPrincipal AnyCaller() => new(new ClaimsIdentity());

    private static WorkloadAction Action(
        string category,
        string employeeName = "Employee",
        DateOnly? dueDate = null,
        Guid? taskId = null,
        Guid? employeeId = null) =>
        new(
            EmployeeId: employeeId ?? Guid.NewGuid(),
            EmployeeName: employeeName,
            Department: "Engineering",
            ActionType: "Do the thing",
            ActionCategory: category,
            DueDate: dueDate,
            AssignedTo: null,
            Status: "Pending",
            DeepLinkUrl: "/companies/x/employees/x/view",
            Urgency: WorkloadActionUrgency.Upcoming,
            TaskId: taskId);

    private static IConfiguration Config(string? timeoutSeconds = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(timeoutSeconds is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["Dashboards:SummaryTimeoutSeconds"] = timeoutSeconds })
            .Build();

    private static DashboardSummaryComposer Composer(
        IEnumerable<IWorkloadActionProvider> providers, IConfiguration? config = null) =>
        new(new FakeServiceScopeFactory([.. providers]), config ?? Config(), new FakeClock(FixedUtcNow));

    private static Task<DashboardSummaryResponse> Compose(
        DashboardSummaryComposer composer, CancellationToken ct = default) =>
        composer.ComposeAsync(Guid.NewGuid(), AnyCaller(), ct);

    [Fact]
    public async Task ComposeAsync_Merges_Multiple_Providers_Into_Per_Category_Results()
    {
        var composer = Composer(
        [
            ConfigurableWorkloadActionProvider.Returning(LeaveCategory, Action(LeaveCategory), Action(LeaveCategory)),
            ConfigurableWorkloadActionProvider.Returning(TasksCategory, Action(TasksCategory)),
        ]);

        var result = await Compose(composer);

        Assert.Equal(2, result.Categories.Count);
        Assert.Equal(2, result.Categories.Single(c => c.Category == LeaveCategory).ActionableCount);
        Assert.Equal(1, result.Categories.Single(c => c.Category == TasksCategory).ActionableCount);
        Assert.Equal(3, result.TotalActionableCount);
        Assert.True(result.AllRequiredLoaded);
        Assert.False(result.HasPartialFailure);
        Assert.Equal(Today, result.AsOfDate);
    }

    [Fact]
    public async Task ComposeAsync_Caps_Items_At_25_And_Flags_Truncated_When_Provider_Returns_More()
    {
        var actions = Enumerable.Range(0, 40).Select(_ => Action(LeaveCategory)).ToArray();
        var composer = Composer([ConfigurableWorkloadActionProvider.Returning(LeaveCategory, actions)]);

        var result = await Compose(composer);
        var category = Assert.Single(result.Categories);

        Assert.Equal(40, category.ActionableCount);
        Assert.Equal(25, category.Items.Count);
        Assert.True(category.IsTruncated);
    }

    [Fact]
    public async Task ComposeAsync_Exactly_25_Items_Is_Not_Truncated()
    {
        var actions = Enumerable.Range(0, 25).Select(_ => Action(LeaveCategory)).ToArray();
        var composer = Composer([ConfigurableWorkloadActionProvider.Returning(LeaveCategory, actions)]);

        var result = await Compose(composer);
        var category = Assert.Single(result.Categories);

        Assert.Equal(25, category.ActionableCount);
        Assert.Equal(25, category.Items.Count);
        Assert.False(category.IsTruncated);
    }

    [Fact]
    public async Task ComposeAsync_Orders_Overdue_First_Then_By_Due_Date_Then_By_Name_With_Nulls_Last()
    {
        var composer = Composer(
        [
            ConfigurableWorkloadActionProvider.Returning(LeaveCategory,
                Action(LeaveCategory, employeeName: "Nadia", dueDate: null),
                Action(LeaveCategory, employeeName: "Bob", dueDate: Today.AddDays(-5)),
                Action(LeaveCategory, employeeName: "charlie", dueDate: Today.AddDays(-1)),
                Action(LeaveCategory, employeeName: "Alice", dueDate: Today.AddDays(-1)),
                Action(LeaveCategory, employeeName: "Amy", dueDate: Today.AddDays(3))),
        ]);

        var items = (await Compose(composer)).Categories.Single().Items;

        Assert.Equal(
            new[] { "Bob", "Alice", "charlie", "Amy", "Nadia" },
            items.Select(i => i.EmployeeName).ToArray());
    }

    [Fact]
    public async Task ComposeAsync_One_Provider_Throwing_Degrades_Only_Its_Category()
    {
        var composer = Composer(
        [
            ConfigurableWorkloadActionProvider.Throwing(LeaveCategory, new InvalidOperationException("boom")),
            ConfigurableWorkloadActionProvider.Returning(TasksCategory,
                Action(TasksCategory), Action(TasksCategory), Action(TasksCategory)),
        ]);

        var result = await Compose(composer);

        var failed = result.Categories.Single(c => c.Category == LeaveCategory);
        Assert.Equal(DashboardCategoryStatus.Failed, failed.Status);
        Assert.Empty(failed.Items);
        Assert.Equal(0, failed.ActionableCount);

        var loaded = result.Categories.Single(c => c.Category == TasksCategory);
        Assert.Equal(DashboardCategoryStatus.Loaded, loaded.Status);
        Assert.Equal(3, loaded.ActionableCount);

        Assert.Equal(3, result.TotalActionableCount); // failed category excluded
        Assert.False(result.AllRequiredLoaded);
        Assert.True(result.HasPartialFailure);
    }

    [Fact]
    public async Task ComposeAsync_All_Providers_Succeed_With_Zero_Actions()
    {
        var composer = Composer(
        [
            ConfigurableWorkloadActionProvider.Returning(LeaveCategory),
            ConfigurableWorkloadActionProvider.Returning(TasksCategory),
        ]);

        var result = await Compose(composer);

        Assert.Equal(0, result.TotalActionableCount);
        Assert.True(result.AllRequiredLoaded);
        Assert.False(result.HasPartialFailure);
        Assert.All(result.Categories, c => Assert.Equal(DashboardCategoryStatus.Loaded, c.Status));
    }

    [Fact]
    public async Task ComposeAsync_Provider_Throwing_Bare_OperationCanceled_While_Outer_Token_Live_Degrades_Category()
    {
        var composer = Composer(
        [
            ConfigurableWorkloadActionProvider.Throwing(LeaveCategory, new OperationCanceledException()),
            ConfigurableWorkloadActionProvider.Returning(TasksCategory, Action(TasksCategory)),
        ]);

        var result = await Compose(composer, CancellationToken.None);

        Assert.Equal(DashboardCategoryStatus.Failed, result.Categories.Single(c => c.Category == LeaveCategory).Status);
        Assert.Equal(DashboardCategoryStatus.Loaded, result.Categories.Single(c => c.Category == TasksCategory).Status);
        Assert.True(result.HasPartialFailure);
    }

    [Fact]
    public async Task ComposeAsync_Provider_Honouring_Deadline_Degrades_Category_Not_The_Whole_Request()
    {
        var composer = Composer(
            [
                ConfigurableWorkloadActionProvider.HonouringDeadline(LeaveCategory),
                ConfigurableWorkloadActionProvider.Returning(TasksCategory, Action(TasksCategory)),
            ],
            Config(timeoutSeconds: "1"));

        var result = await Compose(composer, CancellationToken.None);

        Assert.Equal(DashboardCategoryStatus.Failed, result.Categories.Single(c => c.Category == LeaveCategory).Status);
        Assert.Equal(DashboardCategoryStatus.Loaded, result.Categories.Single(c => c.Category == TasksCategory).Status);
        Assert.False(result.AllRequiredLoaded);
        Assert.True(result.HasPartialFailure);
    }

    [Fact]
    public async Task ComposeAsync_Outer_Client_Cancellation_Propagates_And_Does_Not_Return_Degraded_Envelope()
    {
        var composer = Composer([ConfigurableWorkloadActionProvider.HonouringDeadline(LeaveCategory)]);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Compose(composer, cts.Token));
    }

    [Theory]
    [InlineData(-1, "Overdue", true)]
    [InlineData(0, "DueToday", false)]
    [InlineData(3, "DueThisWeek", false)]
    [InlineData(30, "Upcoming", false)]
    public async Task ComposeAsync_Populates_Urgency_And_IsOverdue_From_DueDate(
        int dueOffsetDays, string expectedUrgency, bool expectedOverdue)
    {
        var composer = Composer(
        [
            ConfigurableWorkloadActionProvider.Returning(LeaveCategory,
                Action(LeaveCategory, dueDate: Today.AddDays(dueOffsetDays))),
        ]);

        var item = (await Compose(composer)).Categories.Single().Items.Single();

        Assert.Equal(expectedUrgency, item.Urgency);
        Assert.Equal(expectedOverdue, item.IsOverdue);
    }

    [Fact]
    public async Task ComposeAsync_Flows_TaskId_Through_To_The_Action_Item()
    {
        var taskId = Guid.NewGuid();
        var composer = Composer(
        [
            ConfigurableWorkloadActionProvider.Returning(TasksCategory, Action(TasksCategory, taskId: taskId)),
        ]);

        var item = (await Compose(composer)).Categories.Single().Items.Single();

        Assert.Equal(taskId, item.TaskId);
    }
}
