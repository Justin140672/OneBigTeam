using HR.Web.Services;

namespace HR.Web.Components.Pages.Dashboards;

public sealed record AttentionQueueItem(
    Guid? EmployeeId,
    string ActionTitle,
    string EmployeeName,
    string Category,
    string StatusLabel,
    DateOnly? DueDate,
    string? DueLabel,
    string DueCss,
    bool IsOverdue,
    int UrgencyRank,
    Guid? TaskId,
    string? DeepLinkUrl,
    bool IsOwnerActionable = true,
    string? OwnerLabel = null)
{
    public bool HasTarget => IsOwnerActionable && (TaskId is not null || !string.IsNullOrWhiteSpace(DeepLinkUrl));

    /// <summary>
    /// True when this row genuinely exists but is intentionally not actionable from the current
    /// list (owned by someone else) — distinct from a stale row whose underlying task/target is
    /// simply gone. Governs which "why can't I click this" message is shown.
    /// </summary>
    public bool IsReadOnlyByDesign => !IsOwnerActionable;

    public string ActionLabel => AttentionQueueSupport.ResolveActionLabel(TaskId, DeepLinkUrl, Category, ActionTitle);

    public string MetaText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(EmployeeName)) parts.Add(EmployeeName);

            if (!string.IsNullOrWhiteSpace(Category) &&
                !(IsOverdue && Category.Contains("Overdue", StringComparison.OrdinalIgnoreCase)))
            {
                parts.Add(Category);
            }

            if (!string.IsNullOrWhiteSpace(StatusLabel) &&
                !StatusLabel.Contains("Overdue", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(StatusLabel);
            }

            if (IsOverdue && DueDate is not null)
                parts.Add($"Due {DueDate.Value:d MMM}");

            return string.Join(" · ", parts);
        }
    }

    public string PriorityCss => IsOverdue ? "critical" : UrgencyRank switch
    {
        1 => "high",
        2 => "medium",
        _ => "low",
    };

    // DSH-07: priority must not be conveyed by colour alone — surface the word in the
    // accessible label and give each level a distinct glyph.
    public string PriorityLabel => PriorityCss switch
    {
        "critical" => "Critical priority",
        "high" => "High priority",
        "medium" => "Medium priority",
        _ => "Low priority",
    };

    public string PriorityIcon => PriorityCss switch
    {
        "critical" => "fa-solid fa-circle-exclamation",
        "high" => "fa-solid fa-triangle-exclamation",
        "medium" => "fa-solid fa-circle",
        _ => "fa-regular fa-circle",
    };

    public string AccessibleLabel =>
        $"{PriorityLabel}. {ActionTitle}, {MetaText}{(DueLabel is null ? "" : $", due {DueLabel}")}. " +
        (HasTarget
            ? $"{ActionLabel}."
            : IsReadOnlyByDesign
                ? $"{OwnerLabel ?? "Owned by someone else"}. Shown for visibility only — not actionable from this list."
                : "This item can no longer be opened — it may have been completed or removed.");
}

public static class AttentionQueueSupport
{
    public static (string? Label, string Css) DueBadge(DateOnly today, DateOnly? due)
    {
        if (due is null) return (null, "");
        return DueBadge(today, due.Value);
    }

    public static (string Label, string Css) DueBadge(DateOnly today, DateOnly due)
    {
        if (due < today) return ("Overdue", "overdue");
        if (due == today) return ("Today", "today");
        if (due == today.AddDays(1)) return ("Tomorrow", "soon");
        if (due <= today.AddDays(7)) return (due.ToString("d MMM"), "soon");
        return (due.ToString("d MMM"), "normal");
    }

    public static string ResolveActionLabel(Guid? taskId, string? deepLinkUrl, string category, string? actionTitle = null)
    {
        if (taskId is not null)
        {
            var c = (category ?? string.Empty).ToLowerInvariant();
            var t = (actionTitle ?? string.Empty).ToLowerInvariant();

            if (c.Contains("leave"))
                return "Review leave request";
            if (c.Contains("probation"))
                return "Review probation";
            if (t.Contains("return to work"))
                return "Complete return-to-work review";
            if (t.Contains("evidence"))
                return "View evidence request";

            return "Open task";
        }

        var url = deepLinkUrl?.ToLowerInvariant() ?? string.Empty;
        if (url.Contains("acknowledge"))
            return "View acknowledgement progress";
        if (url.Contains("/documents") || url.Contains("/document/"))
            return "View document";
        if (url.Contains("/user-administration") || url.Contains("/users") || url.Contains("/user-accounts") || url.Contains("/accounts"))
            return "Review user account";
        if (url.Contains("/employees") || url.Contains("/employee/"))
            return "View employee";

        return (category ?? string.Empty).ToLowerInvariant() switch
        {
            var c when c.Contains("acknowledg") => "View acknowledgement progress",
            var c when c.Contains("document") => "View document",
            var c when c.Contains("account") || c.Contains("access") => "Review user account",
            var c when c.Contains("employee") || c.Contains("compliance") || c.Contains("wellbeing") => "View employee",
            _ => "View details",
        };
    }

    public static AttentionQueueItem ToAttentionItem(DashboardActionItemModel it, DateOnly today)
    {
        var (dueLabel, dueCss) = DueBadge(today, it.DueDate);
        return new AttentionQueueItem(
            EmployeeId: it.EmployeeId,
            ActionTitle: string.IsNullOrWhiteSpace(it.ActionType) ? it.Category : it.ActionType,
            EmployeeName: it.EmployeeName,
            Category: it.Category,
            StatusLabel: string.IsNullOrWhiteSpace(it.Status) ? it.ActionType : it.Status,
            DueDate: it.DueDate,
            DueLabel: it.IsOverdue ? "Overdue" : dueLabel,
            DueCss: it.IsOverdue ? "overdue" : dueCss,
            IsOverdue: it.IsOverdue,
            UrgencyRank: it.IsOverdue ? 0 : it.UrgencyRank,
            TaskId: it.TaskId,
            DeepLinkUrl: it.DeepLinkUrl,
            IsOwnerActionable: it.IsOwnerActionable,
            OwnerLabel: it.OwnerLabel);
    }

    public static (IReadOnlyList<WidgetSourceOutcome> Outcomes, IReadOnlyList<AttentionQueueItem> Items) Convert(
        DashboardSummaryModel response, DateOnly today)
    {
        var outcomes = new List<WidgetSourceOutcome>();
        var items = new List<AttentionQueueItem>();

        foreach (var category in response.Categories)
        {
            outcomes.Add(new WidgetSourceOutcome(
                category.Category, true, category.IsFailed, category.ActionableCount));

            if (category.IsFailed) continue;

            foreach (var it in category.Items)
                items.Add(ToAttentionItem(it, today));
        }

        return (outcomes, items);
    }
}
