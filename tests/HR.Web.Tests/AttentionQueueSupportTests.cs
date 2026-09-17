using HR.Web.Components.Pages.Dashboards;
using HR.Web.Services;

namespace HR.Web.Tests;

/// <summary>
/// Shared logic extracted from the former duplicated AttentionQueueWidget / ManagerAttentionQueueWidget
/// (HR + manager dashboard "attention queue" panels) into <see cref="AttentionQueueSupport"/> and
/// <see cref="AttentionQueueItem"/>. Per this repo's convention, bUnit is not used (no existing
/// component-test pattern in tests/HR.Web.Tests) — these tests exercise the pure, Blazor-agnostic
/// view-model/classification/conversion functions directly.
/// </summary>
public class AttentionQueueSupportTests
{
    private static readonly DateOnly Today = new(2026, 9, 16);

    // ---------- DueBadge boundary tests ----------

    [Fact]
    public void DueBadge_DateBeforeToday_IsOverdue()
    {
        var (label, css) = AttentionQueueSupport.DueBadge(Today, Today.AddDays(-1));
        Assert.Equal("Overdue", label);
        Assert.Equal("overdue", css);
    }

    [Fact]
    public void DueBadge_ExactlyToday_IsToday()
    {
        var (label, css) = AttentionQueueSupport.DueBadge(Today, Today);
        Assert.Equal("Today", label);
        Assert.Equal("today", css);
    }

    [Fact]
    public void DueBadge_ExactlyTomorrow_IsTomorrow()
    {
        var (label, css) = AttentionQueueSupport.DueBadge(Today, Today.AddDays(1));
        Assert.Equal("Tomorrow", label);
        Assert.Equal("soon", css);
    }

    [Fact]
    public void DueBadge_ExactlyTodayPlusSeven_IsStillSoon()
    {
        var due = Today.AddDays(7);
        var (label, css) = AttentionQueueSupport.DueBadge(Today, due);
        Assert.Equal(due.ToString("d MMM"), label);
        Assert.Equal("soon", css);
    }

    [Fact]
    public void DueBadge_TodayPlusEight_IsNormal()
    {
        var due = Today.AddDays(8);
        var (label, css) = AttentionQueueSupport.DueBadge(Today, due);
        Assert.Equal(due.ToString("d MMM"), label);
        Assert.Equal("normal", css);
    }

    [Fact]
    public void DueBadge_NullableOverload_NullDueDate_ReturnsNullLabelAndEmptyCss()
    {
        var (label, css) = AttentionQueueSupport.DueBadge(Today, (DateOnly?)null);
        Assert.Null(label);
        Assert.Equal("", css);
    }

    [Fact]
    public void DueBadge_NullableOverload_WithValue_DelegatesToNonNullableOverload()
    {
        var (label, css) = AttentionQueueSupport.DueBadge(Today, (DateOnly?)Today);
        Assert.Equal("Today", label);
        Assert.Equal("today", css);
    }

    // ---------- ResolveActionLabel ----------

    [Fact]
    public void ResolveActionLabel_TaskIdPresent_AlwaysReturnsOpenTask()
    {
        // Even when the deep link/category would otherwise resolve to something else, a task wins.
        var label = AttentionQueueSupport.ResolveActionLabel(Guid.NewGuid(), "/employees/123", "SomethingElse");
        Assert.Equal("Open task", label);
    }

    [Theory]
    [InlineData("/policies/1/acknowledge", "View acknowledgement progress")]
    [InlineData("/documents/1", "View document")]
    [InlineData("/document/1", "View document")]
    [InlineData("/user-administration/users/1", "Review user account")]
    [InlineData("/users/1", "Review user account")]
    [InlineData("/user-accounts/1", "Review user account")]
    [InlineData("/accounts/1", "Review user account")]
    [InlineData("/employees/1", "View employee")]
    [InlineData("/employee/1", "View employee")]
    public void ResolveActionLabel_NoTaskId_DeepLinkKeywordBranches(string deepLink, string expected)
    {
        var label = AttentionQueueSupport.ResolveActionLabel(null, deepLink, category: "Unrelated");
        Assert.Equal(expected, label);
    }

    [Theory]
    [InlineData("Policy Acknowledgement", "View acknowledgement progress")]
    [InlineData("Document Review", "View document")]
    [InlineData("Account Access Review", "Review user account")]
    [InlineData("System Access", "Review user account")]
    [InlineData("Employee Onboarding", "View employee")]
    [InlineData("Compliance Check", "View employee")]
    [InlineData("Wellbeing Check-in", "View employee")]
    public void ResolveActionLabel_NoTaskIdNoDeepLink_CategoryFallbackBranches(string category, string expected)
    {
        var label = AttentionQueueSupport.ResolveActionLabel(null, null, category);
        Assert.Equal(expected, label);
    }

    [Fact]
    public void ResolveActionLabel_NoTaskIdNoMatchingKeyword_DefaultsToViewDetails()
    {
        var label = AttentionQueueSupport.ResolveActionLabel(null, "/somewhere/else", "SomethingUnmatched");
        Assert.Equal("View details", label);
    }

    [Fact]
    public void ResolveActionLabel_NullDeepLinkAndUnmatchedCategory_DefaultsToViewDetails()
    {
        var label = AttentionQueueSupport.ResolveActionLabel(null, null, "Unmatched");
        Assert.Equal("View details", label);
    }

    // ---------- AttentionQueueItem computed properties ----------

    private static AttentionQueueItem Item(
        Guid? employeeId = null,
        string actionTitle = "Review policy",
        string employeeName = "Jane Doe",
        string category = "Task",
        string statusLabel = "Pending",
        DateOnly? dueDate = null,
        string? dueLabel = null,
        string dueCss = "normal",
        bool isOverdue = false,
        int urgencyRank = 3,
        Guid? taskId = null,
        string? deepLinkUrl = null) =>
        new(employeeId, actionTitle, employeeName, category, statusLabel, dueDate, dueLabel, dueCss,
            isOverdue, urgencyRank, taskId, deepLinkUrl);

    [Fact]
    public void HasTarget_TaskIdPresent_IsTrue()
    {
        Assert.True(Item(taskId: Guid.NewGuid(), deepLinkUrl: null).HasTarget);
    }

    [Fact]
    public void HasTarget_NonBlankDeepLink_IsTrue()
    {
        Assert.True(Item(taskId: null, deepLinkUrl: "/employees/1").HasTarget);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HasTarget_NoTaskIdAndBlankDeepLink_IsFalse(string? deepLink)
    {
        Assert.False(Item(taskId: null, deepLinkUrl: deepLink).HasTarget);
    }

    [Fact]
    public void PriorityCss_Overdue_IsCritical_RegardlessOfUrgencyRank()
    {
        Assert.Equal("critical", Item(isOverdue: true, urgencyRank: 3).PriorityCss);
    }

    [Fact]
    public void PriorityCss_NotOverdue_UrgencyRank1_IsHigh()
    {
        Assert.Equal("high", Item(isOverdue: false, urgencyRank: 1).PriorityCss);
    }

    [Fact]
    public void PriorityCss_NotOverdue_UrgencyRank2_IsMedium()
    {
        Assert.Equal("medium", Item(isOverdue: false, urgencyRank: 2).PriorityCss);
    }

    [Fact]
    public void PriorityCss_NotOverdue_UrgencyRankOther_IsLow()
    {
        Assert.Equal("low", Item(isOverdue: false, urgencyRank: 3).PriorityCss);
        Assert.Equal("low", Item(isOverdue: false, urgencyRank: 0).PriorityCss);
    }

    [Theory]
    [InlineData(true, 3, "Critical priority", "fa-solid fa-circle-exclamation")]
    [InlineData(false, 1, "High priority", "fa-solid fa-triangle-exclamation")]
    [InlineData(false, 2, "Medium priority", "fa-solid fa-circle")]
    [InlineData(false, 3, "Low priority", "fa-regular fa-circle")]
    public void PriorityLabelAndIcon_MatchPriorityCss(bool isOverdue, int urgencyRank, string expectedLabel, string expectedIcon)
    {
        var item = Item(isOverdue: isOverdue, urgencyRank: urgencyRank);
        Assert.Equal(expectedLabel, item.PriorityLabel);
        Assert.Equal(expectedIcon, item.PriorityIcon);
    }

    [Fact]
    public void MetaText_ComposesEmployeeCategoryAndStatus_WhenNotOverdue()
    {
        var item = Item(employeeName: "Jane Doe", category: "Task", statusLabel: "Pending", isOverdue: false);
        Assert.Equal("Jane Doe · Task · Pending", item.MetaText);
    }

    [Fact]
    public void MetaText_OmitsBlankParts()
    {
        var item = Item(employeeName: "", category: "Task", statusLabel: "   ", isOverdue: false);
        Assert.Equal("Task", item.MetaText);
    }

    [Fact]
    public void MetaText_Overdue_CategoryContainingOverdue_IsDropped_ButDueDateAppended()
    {
        var due = new DateOnly(2026, 9, 1);
        var item = Item(
            employeeName: "Jane Doe",
            category: "Manager Tasks Overdue",
            statusLabel: "In progress",
            isOverdue: true,
            dueDate: due);

        // Category repeats "Overdue" -> dropped. Status doesn't -> kept. Due date appended because overdue.
        Assert.Equal($"Jane Doe · In progress · Due {due:d MMM}", item.MetaText);
    }

    [Fact]
    public void MetaText_Overdue_StatusLabelContainingOverdue_IsDropped()
    {
        var due = new DateOnly(2026, 9, 1);
        var item = Item(
            employeeName: "Jane Doe",
            category: "Task",
            statusLabel: "Overdue",
            isOverdue: true,
            dueDate: due);

        Assert.Equal($"Jane Doe · Task · Due {due:d MMM}", item.MetaText);
    }

    [Fact]
    public void MetaText_Overdue_WithoutDueDate_DoesNotAppendDuePart()
    {
        var item = Item(employeeName: "Jane Doe", category: "Task", statusLabel: "Pending", isOverdue: true, dueDate: null);
        Assert.Equal("Jane Doe · Task · Pending", item.MetaText);
    }

    [Fact]
    public void AccessibleLabel_TaskBackedItem_IncludesActionLabel()
    {
        var item = Item(
            actionTitle: "Review policy",
            employeeName: "Jane Doe",
            category: "Task",
            statusLabel: "Pending",
            isOverdue: false,
            urgencyRank: 1,
            taskId: Guid.NewGuid(),
            dueLabel: "Tomorrow");

        Assert.Equal(
            "High priority. Review policy, Jane Doe · Task · Pending, due Tomorrow. Open task.",
            item.AccessibleLabel);
    }

    [Fact]
    public void AccessibleLabel_StaleItem_NoTarget_UsesFallbackSentence()
    {
        var item = Item(
            actionTitle: "Review policy",
            employeeName: "Jane Doe",
            category: "Task",
            statusLabel: "Pending",
            isOverdue: false,
            urgencyRank: 3,
            taskId: null,
            deepLinkUrl: null,
            dueLabel: null);

        Assert.Equal(
            "Low priority. Review policy, Jane Doe · Task · Pending. This item can no longer be opened — it may have been completed or removed.",
            item.AccessibleLabel);
    }

    // ---------- Convert ----------

    private static DashboardActionItemModel RawItem(
        Guid? employeeId = null,
        string employeeName = "Jane Doe",
        string actionType = "Review",
        string category = "Task",
        DateOnly? dueDate = null,
        string urgency = "DueThisWeek",
        bool isOverdue = false,
        string status = "Pending",
        string deepLinkUrl = "/tasks/1",
        Guid? taskId = null) =>
        new(employeeId, employeeName, Department: null, actionType, category, dueDate, urgency,
            isOverdue, status, deepLinkUrl, taskId);

    private static DashboardCategoryModel Category(
        string name,
        bool required,
        bool failed,
        int actionableCount,
        IReadOnlyList<DashboardActionItemModel>? items = null) =>
        new(name, failed ? "Failed" : "Ok", required, actionableCount, IsTruncated: false, items ?? []);

    [Fact]
    public void Convert_SuccessfulCategory_MapsToOutcomeAndItems()
    {
        var response = new DashboardSummaryModel(
            Categories:
            [
                Category("Leave", required: true, failed: false, actionableCount: 1,
                    items: [RawItem(employeeName: "A")]),
            ],
            TotalActionableCount: 1, AllRequiredLoaded: true, HasPartialFailure: false, AsOfDate: Today);

        var (outcomes, items) = AttentionQueueSupport.Convert(response, Today);

        var outcome = Assert.Single(outcomes);
        Assert.Equal("Leave", outcome.SourceName);
        Assert.True(outcome.Required);
        Assert.False(outcome.Failed);
        Assert.Equal(1, outcome.ActionableCount);

        var item = Assert.Single(items);
        Assert.Equal("A", item.EmployeeName);
    }

    [Fact]
    public void Convert_FailedCategory_AppearsInOutcomes_ButContributesZeroItems()
    {
        var response = new DashboardSummaryModel(
            Categories:
            [
                Category("Documents", required: true, failed: true, actionableCount: 0,
                    items: [RawItem(employeeName: "Should not appear")]),
            ],
            TotalActionableCount: 0, AllRequiredLoaded: false, HasPartialFailure: true, AsOfDate: Today);

        var (outcomes, items) = AttentionQueueSupport.Convert(response, Today);

        var outcome = Assert.Single(outcomes);
        Assert.Equal("Documents", outcome.SourceName);
        Assert.True(outcome.Failed);
        Assert.Empty(items);
    }

    [Fact]
    public void Convert_MixedSuccessAndFailure_OnlySuccessfulCategoryContributesItems()
    {
        var response = new DashboardSummaryModel(
            Categories:
            [
                Category("Leave", required: true, failed: false, actionableCount: 2,
                    items: [RawItem(employeeName: "A"), RawItem(employeeName: "B")]),
                Category("Documents", required: true, failed: true, actionableCount: 0,
                    items: [RawItem(employeeName: "C")]),
            ],
            TotalActionableCount: 2, AllRequiredLoaded: false, HasPartialFailure: true, AsOfDate: Today);

        var (outcomes, items) = AttentionQueueSupport.Convert(response, Today);

        Assert.Equal(2, outcomes.Count);
        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, i => i.EmployeeName == "C");
    }

    [Fact]
    public void Convert_NoCategories_ReturnsEmptyOutcomesAndItems()
    {
        var response = new DashboardSummaryModel(
            Categories: [], TotalActionableCount: 0, AllRequiredLoaded: true, HasPartialFailure: false, AsOfDate: Today);

        var (outcomes, items) = AttentionQueueSupport.Convert(response, Today);

        Assert.Empty(outcomes);
        Assert.Empty(items);
    }

    [Fact]
    public void Convert_ToAttentionItem_OverdueRawItem_ForcesUrgencyRankZeroAndOverdueBadge()
    {
        var response = new DashboardSummaryModel(
            Categories:
            [
                Category("Leave", required: true, failed: false, actionableCount: 1,
                    items: [RawItem(isOverdue: true, urgency: "DueThisWeek", dueDate: Today.AddDays(-3))]),
            ],
            TotalActionableCount: 1, AllRequiredLoaded: true, HasPartialFailure: false, AsOfDate: Today);

        var (_, items) = AttentionQueueSupport.Convert(response, Today);
        var item = Assert.Single(items);

        Assert.True(item.IsOverdue);
        Assert.Equal(0, item.UrgencyRank);
        Assert.Equal("Overdue", item.DueLabel);
        Assert.Equal("overdue", item.DueCss);
    }

    [Fact]
    public void Convert_ToAttentionItem_BlankActionTypeAndStatus_FallsBackToCategoryAndActionType()
    {
        var response = new DashboardSummaryModel(
            Categories:
            [
                Category("Leave requests", required: true, failed: false, actionableCount: 1,
                    items: [RawItem(actionType: "", category: "Leave requests", status: "")]),
            ],
            TotalActionableCount: 1, AllRequiredLoaded: true, HasPartialFailure: false, AsOfDate: Today);

        var (_, items) = AttentionQueueSupport.Convert(response, Today);
        var item = Assert.Single(items);

        // ActionTitle falls back to Category when ActionType is blank.
        Assert.Equal("Leave requests", item.ActionTitle);
        // StatusLabel falls back to ActionType (also blank here, so ends up empty).
        Assert.Equal("", item.StatusLabel);
    }

    // ---------- Manager per-employee alert-count dictionary construction ----------
    // (mirrors ManagerAttentionQueueWidget.RecomputeAsync's inline dictionary build, which is not
    // itself extracted into a testable pure function — see report.)

    [Fact]
    public void AlertCountDictionary_BuiltFromConvertedItems_CountsPerEmployee_SkippingNullEmployeeId()
    {
        var employeeA = Guid.NewGuid();
        var employeeB = Guid.NewGuid();

        var response = new DashboardSummaryModel(
            Categories:
            [
                Category("Leave", required: true, failed: false, actionableCount: 4,
                    items:
                    [
                        RawItem(employeeId: employeeA),
                        RawItem(employeeId: employeeA),
                        RawItem(employeeId: employeeB),
                        RawItem(employeeId: null),
                    ]),
            ],
            TotalActionableCount: 4, AllRequiredLoaded: true, HasPartialFailure: false, AsOfDate: Today);

        var (_, items) = AttentionQueueSupport.Convert(response, Today);

        // Same construction as ManagerAttentionQueueWidget.RecomputeAsync: group raw items by
        // EmployeeId, skipping items with no employee, into a count-per-employee dictionary.
        var counts = new Dictionary<Guid, int>();
        foreach (var item in items)
        {
            if (item.EmployeeId is not { } id) continue;
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }

        Assert.Equal(2, counts.Count);
        Assert.Equal(2, counts[employeeA]);
        Assert.Equal(1, counts[employeeB]);
    }

    [Fact]
    public void AlertCountDictionary_TotalFailure_ProducesEmptyDictionary()
    {
        var response = new DashboardSummaryModel(
            Categories: [Category("Leave", required: true, failed: true, actionableCount: 0)],
            TotalActionableCount: 0, AllRequiredLoaded: false, HasPartialFailure: false, AsOfDate: Today);

        var (_, items) = AttentionQueueSupport.Convert(response, Today);

        var counts = new Dictionary<Guid, int>();
        foreach (var item in items)
        {
            if (item.EmployeeId is not { } id) continue;
            counts[id] = counts.GetValueOrDefault(id) + 1;
        }

        Assert.Empty(counts);
        Assert.Equal(0, items.Count);
    }
}
