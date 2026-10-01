using System.Security.Claims;
using HR.Infrastructure.Abstractions;
using HR.Modules.Reporting.Features.DashboardSummaries;
using HR.Modules.Reporting.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Reporting.Tests;

public class DashboardSummaryComposerActionabilityTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 8, 30, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(FixedUtcNow);
    private static readonly Guid CompanyId = Guid.NewGuid();

    private const string Category = "Outstanding Onboarding Tasks";

    private static WorkloadAction Action(
        string name = "Employee",
        Guid? taskId = null,
        string deepLink = "",
        bool isOwnerActionable = true,
        WorkloadActionability? @override = null,
        DateOnly? dueDate = null,
        string? ownerLabel = null,
        string? reason = null,
        Guid? employeeId = null) =>
        new(
            EmployeeId: employeeId ?? Guid.NewGuid(),
            EmployeeName: name,
            Department: "Ops",
            ActionType: "Do the thing",
            ActionCategory: Category,
            DueDate: dueDate,
            AssignedTo: null,
            Status: "Pending",
            DeepLinkUrl: deepLink,
            TaskId: taskId,
            IsOwnerActionable: isOwnerActionable,
            OwnerLabel: ownerLabel,
            ActionabilityOverride: @override,
            VisibilityReason: reason);

    private static Task<DashboardSummaryResponse> ComposeAsync(WorkloadScope scope, params WorkloadAction[] actions)
    {
        var composer = new DashboardSummaryComposer(
            new FakeServiceScopeFactory([ConfigurableWorkloadActionProvider.Returning(Category, actions)]),
            new ConfigurationBuilder().Build(),
            new FakeClock(FixedUtcNow));

        return composer.ComposeAsync(CompanyId, new ClaimsPrincipal(new ClaimsIdentity()), scope, CancellationToken.None);
    }

    [Fact]
    public async Task Splits_Actionable_And_WaitingOnOthers_With_Separate_Counts()
    {
        var result = await ComposeAsync(
            WorkloadScope.Hr,
            Action("Mine", taskId: Guid.NewGuid()),
            Action("Mine too", deepLink: "/companies/x/employees/y"),
            Action("Theirs", taskId: Guid.NewGuid(), isOwnerActionable: false, ownerLabel: "Assigned to Sam"));

        var category = Assert.Single(result.Categories);
        Assert.Equal(2, category.ActionableCount);
        Assert.Equal(1, category.WaitingOnOthersCount);
        Assert.Equal(2, result.TotalActionableCount);
        Assert.Equal(1, result.TotalWaitingOnOthersCount);
        Assert.Equal(2, category.Items.Count);
        Assert.Single(category.WaitingItems);
    }

    [Fact]
    public async Task Waiting_Items_Never_Carry_An_Actionable_Destination()
    {
        var employeeId = Guid.NewGuid();
        var result = await ComposeAsync(
            WorkloadScope.Hr,
            Action("Theirs", taskId: Guid.NewGuid(), deepLink: "/companies/x/tasks/1", isOwnerActionable: false,
                ownerLabel: "Assigned to Sam", reason: "Shown so you can monitor it.", employeeId: employeeId));

        var item = Assert.Single(Assert.Single(result.Categories).WaitingItems);
        Assert.Null(item.TaskId);
        Assert.Equal(string.Empty, item.DeepLinkUrl);
        Assert.False(item.IsOwnerActionable);
        Assert.Equal(DashboardActionability.VisibilityOnly, item.Actionability);
        Assert.Equal("Assigned to Sam", item.OwnerLabel);
        Assert.Equal("Shown so you can monitor it.", item.VisibilityReason);
        Assert.Equal($"/companies/{CompanyId}/employees/{employeeId}", item.MonitoringUrl);
    }

    [Fact]
    public async Task Overdue_Waiting_Item_Keeps_Overdue_Styling_Without_Becoming_Actionable()
    {
        var result = await ComposeAsync(
            WorkloadScope.Hr,
            Action("Late", taskId: Guid.NewGuid(), isOwnerActionable: false, dueDate: Today.AddDays(-4)));

        var item = Assert.Single(Assert.Single(result.Categories).WaitingItems);
        Assert.True(item.IsOverdue);
        Assert.Equal("Overdue", item.Urgency);
        Assert.Equal(0, result.TotalActionableCount);
    }

    [Fact]
    public async Task Actionable_Item_Without_A_Destination_Is_Excluded_From_Both_Queues_And_Reported_As_An_Exception()
    {
        var employeeId = Guid.NewGuid();
        var result = await ComposeAsync(
            WorkloadScope.Hr,
            Action("Orphan", employeeId: employeeId),
            Action("Healthy", taskId: Guid.NewGuid()));

        var category = Assert.Single(result.Categories);
        Assert.Equal(1, category.ActionableCount);
        Assert.Equal(0, category.WaitingOnOthersCount);
        Assert.Equal(1, category.UnavailableCount);
        Assert.Equal(1, result.TotalUnavailableCount);

        var exception = Assert.Single(result.Exceptions);
        Assert.Equal(Category, exception.Category);
        Assert.Equal("Orphan", exception.EmployeeName);
        Assert.Equal($"/companies/{CompanyId}/employees/{employeeId}", exception.InvestigationUrl);
        Assert.Contains("administrator investigation", exception.Message);
    }

    [Fact]
    public async Task Explicitly_Unavailable_Item_Is_Not_Counted_And_Uses_The_Providers_Explanation()
    {
        var result = await ComposeAsync(
            WorkloadScope.Hr,
            Action("Missing record", @override: WorkloadActionability.Unavailable, employeeId: Guid.Empty,
                reason: "Unable to load the related sickness evidence request. This item may require administrator investigation."));

        var category = Assert.Single(result.Categories);
        Assert.Equal(0, category.ActionableCount);
        Assert.Equal(0, category.WaitingOnOthersCount);
        Assert.Equal(1, category.UnavailableCount);

        var exception = Assert.Single(result.Exceptions);
        Assert.Equal(
            "Unable to load the related sickness evidence request. This item may require administrator investigation.",
            exception.Message);
        Assert.Null(exception.InvestigationUrl);
        Assert.Null(exception.EmployeeName);
    }

    [Fact]
    public async Task Manager_Scope_Counts_Unavailable_Items_But_Does_Not_Return_Exception_Rows()
    {
        var result = await ComposeAsync(WorkloadScope.Manager, Action("Orphan"));

        Assert.Equal(1, result.TotalUnavailableCount);
        Assert.Empty(result.Exceptions);
        Assert.Equal(0, result.TotalActionableCount);
    }

    [Fact]
    public async Task Priority_And_Urgency_Do_Not_Change_Ownership()
    {
        var result = await ComposeAsync(
            WorkloadScope.Hr,
            Action("Critical but theirs", taskId: Guid.NewGuid(), isOwnerActionable: false, dueDate: Today.AddDays(-30)),
            Action("Low but mine", taskId: Guid.NewGuid(), dueDate: Today.AddDays(30)));

        var category = Assert.Single(result.Categories);
        Assert.Equal("Low but mine", Assert.Single(category.Items).EmployeeName);
        Assert.Equal("Critical but theirs", Assert.Single(category.WaitingItems).EmployeeName);
    }

    [Fact]
    public async Task Each_Queue_Is_Capped_Independently_With_Full_Counts()
    {
        var actionable = Enumerable.Range(0, 30).Select(i => Action($"A{i}", taskId: Guid.NewGuid()));
        var waiting = Enumerable.Range(0, 27).Select(i => Action($"W{i}", taskId: Guid.NewGuid(), isOwnerActionable: false));

        var result = await ComposeAsync(WorkloadScope.Hr, [.. actionable, .. waiting]);

        var category = Assert.Single(result.Categories);
        Assert.Equal(30, category.ActionableCount);
        Assert.Equal(25, category.Items.Count);
        Assert.True(category.IsTruncated);
        Assert.Equal(27, category.WaitingOnOthersCount);
        Assert.Equal(25, category.WaitingItems.Count);
        Assert.True(category.WaitingIsTruncated);
    }

    [Theory]
    [InlineData(true, true, WorkloadActionability.CanAct)]
    [InlineData(true, false, WorkloadActionability.Unavailable)]
    public void Classify_Requires_A_Destination_To_Be_Actionable(bool ownerActionable, bool hasDestination, WorkloadActionability expected)
    {
        var action = Action(taskId: hasDestination ? Guid.NewGuid() : null, isOwnerActionable: ownerActionable);

        Assert.Equal(expected, DashboardSummaryComposer.Classify(action));
    }

    [Fact]
    public void Classify_Never_Promotes_A_VisibilityOnly_Item_Even_With_A_Destination()
    {
        var action = Action(taskId: Guid.NewGuid(), deepLink: "/x", isOwnerActionable: false);

        Assert.Equal(WorkloadActionability.VisibilityOnly, DashboardSummaryComposer.Classify(action));
    }
}
