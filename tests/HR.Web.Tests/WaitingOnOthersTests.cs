using HR.Web.Components.Pages.Dashboards;
using HR.Web.Services;

namespace HR.Web.Tests;

public class WaitingOnOthersTests
{
    private static readonly DateOnly Today = new(2026, 9, 30);

    private static DashboardActionItemModel Item(
        string title,
        bool canAct,
        Guid? taskId = null,
        string deepLink = "",
        string? ownerLabel = null,
        string? monitoringUrl = null,
        bool overdue = false) =>
        new(
            EmployeeId: Guid.NewGuid(),
            EmployeeName: "Employee",
            Department: null,
            ActionType: title,
            Category: "Pending Leave Approvals",
            DueDate: overdue ? Today.AddDays(-2) : Today.AddDays(2),
            Urgency: overdue ? "Overdue" : "DueThisWeek",
            IsOverdue: overdue,
            Status: "Pending",
            DeepLinkUrl: deepLink,
            TaskId: taskId,
            IsOwnerActionable: canAct,
            OwnerLabel: ownerLabel,
            Actionability: canAct ? "CanAct" : "VisibilityOnly",
            VisibilityReason: canAct ? null : "Shown so you can monitor it.",
            MonitoringUrl: monitoringUrl);

    private static DashboardSummaryModel Summary(
        IReadOnlyList<DashboardActionItemModel> items,
        IReadOnlyList<DashboardActionItemModel> waiting,
        int actionableCount,
        int waitingCount) =>
        new(
            [
                new DashboardCategoryModel(
                    "Pending Leave Approvals", "Loaded", true, actionableCount, false, items,
                    waiting, waitingCount),
            ],
            TotalActionableCount: actionableCount,
            AllRequiredLoaded: true,
            HasPartialFailure: false,
            AsOfDate: Today,
            TotalWaitingOnOthersCount: waitingCount);

    [Fact]
    public void Convert_Returns_Only_Actionable_Items_And_The_Actionable_Count()
    {
        var response = Summary(
            [Item("Mine", canAct: true, taskId: Guid.NewGuid())],
            [Item("Theirs", canAct: false, ownerLabel: "Assigned to Sam")],
            actionableCount: 1,
            waitingCount: 1);

        var (outcomes, items) = AttentionQueueSupport.Convert(response, Today);

        var item = Assert.Single(items);
        Assert.Equal("Mine", item.ActionTitle);
        Assert.True(item.HasTarget);
        Assert.Equal(1, Assert.Single(outcomes).ActionableCount);
        Assert.Equal(1, WidgetPanelState.Summarise(outcomes).TotalActionableCount);
    }

    [Fact]
    public void ConvertWaiting_Returns_Visibility_Only_Items_That_Never_Have_An_Actionable_Target()
    {
        var response = Summary(
            [],
            [Item("Theirs", canAct: false, ownerLabel: "Assigned to Sam", monitoringUrl: "/companies/c/employees/e", overdue: true)],
            actionableCount: 0,
            waitingCount: 1);

        var waiting = Assert.Single(AttentionQueueSupport.ConvertWaiting(response, Today));

        Assert.False(waiting.HasTarget);
        Assert.True(waiting.IsReadOnlyByDesign);
        Assert.Equal("Assigned to Sam", waiting.OwnerLabel);
        Assert.Equal("/companies/c/employees/e", waiting.MonitoringUrl);
        Assert.Equal("Shown so you can monitor it.", waiting.VisibilityReason);
        Assert.True(waiting.IsOverdue);
        Assert.Equal("critical", waiting.PriorityCss);
    }

    [Fact]
    public void ConvertWaiting_Skips_Failed_Categories()
    {
        var response = new DashboardSummaryModel(
            [new DashboardCategoryModel("Pending Leave Approvals", "Failed", true, 0, false, [], [Item("Theirs", false)], 1)],
            0, false, false, Today);

        Assert.Empty(AttentionQueueSupport.ConvertWaiting(response, Today));
    }

    [Fact]
    public void Empty_Queues_Are_Reported_Independently()
    {
        var response = Summary([], [], actionableCount: 0, waitingCount: 0);

        var (outcomes, items) = AttentionQueueSupport.Convert(response, Today);

        Assert.True(WidgetPanelState.Summarise(outcomes).ShowAllClear);
        Assert.Empty(items);
        Assert.Empty(AttentionQueueSupport.ConvertWaiting(response, Today));
    }

    [Fact]
    public void Waiting_Panel_Markup_Has_No_Interactive_Row_Elements()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "HR.Web", "Components", "Pages", "Dashboards", "WaitingOnOthersPanel.razor"));

        Assert.DoesNotContain("<button", source);
        Assert.DoesNotContain("@onclick", source);
        Assert.DoesNotContain("TaskId", source);
        Assert.Contains("Responsible:", source);
        Assert.Contains("Nothing is waiting on anyone else right now.", source);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))
                && (dir.GetFiles("*.sln").Length > 0 || dir.GetFiles("*.slnx").Length > 0))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
