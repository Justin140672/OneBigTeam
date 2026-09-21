using HR.Web.Services;

namespace HR.Web.Components.Pages.Dashboards;

/// <summary>
/// Blazor-agnostic view model for one row in an attention-queue widget (shared by
/// <see cref="AttentionQueueWidget"/> and <see cref="ManagerAttentionQueueWidget"/> via
/// <see cref="AttentionQueuePanel"/>). Carries everything the shared presentation needs to render
/// a row and decide how it activates — either opening a task dialog (<see cref="TaskId"/>) or
/// navigating to a deep link (<see cref="DeepLinkUrl"/>).
/// </summary>
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
    /// <summary>
    /// An item is actionable only if it is owned by the viewer in the workspace being displayed
    /// AND it opens a task or has a non-blank deep link. Actionability is never inferred from mere
    /// visibility — a row can be shown for oversight (<see cref="IsOwnerActionable"/> false) without
    /// being clickable, e.g. a manager-owned leave approval shown to HR for company-wide visibility.
    /// </summary>
    public bool HasTarget => IsOwnerActionable && (TaskId is not null || !string.IsNullOrWhiteSpace(DeepLinkUrl));

    /// <summary>
    /// True when this row genuinely exists but is intentionally not actionable from the current
    /// list (owned by someone else) — distinct from a stale row whose underlying task/target is
    /// simply gone. Governs which "why can't I click this" message is shown.
    /// </summary>
    public bool IsReadOnlyByDesign => !IsOwnerActionable;

    public string ActionLabel => AttentionQueueSupport.ResolveActionLabel(TaskId, DeepLinkUrl, Category, ActionTitle);

    /// <summary>
    /// Secondary/supporting line under the task title: employee, source category, and (when
    /// meaningful) status — never repeating "Overdue" alongside the due-badge, and adding the
    /// exact due date for overdue rows so same-employee/same-title items stay distinguishable.
    /// </summary>
    public string MetaText
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(EmployeeName)) parts.Add(EmployeeName);

            // When the row is overdue, the due-badge already reads "Overdue" — drop any
            // supporting part (category name, status) that would repeat the word rather than
            // only guarding against an exact "Overdue" status match (e.g. category names like
            // "Manager Tasks Overdue" would otherwise duplicate it).
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

/// <summary>
/// Pure, Blazor-agnostic helper functions shared by every attention-queue widget: due-date
/// classification/labelling and category-outcome/action-item conversion. Extracted from the
/// former per-widget duplicated logic so behaviour (and its tests) live in one place.
/// </summary>
public static class AttentionQueueSupport
{
    /// <summary>Classifies a due date relative to <paramref name="today"/> into a label and CSS suffix.</summary>
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

    /// <summary>
    /// Resolves the visible action label for an attention row. One naming convention is used:
    /// "&lt;verb&gt; &lt;destination noun&gt;" (e.g. "View employee", "View document",
    /// "Review user account", "View acknowledgement progress"). A row backed by a task always
    /// opens the task dialog, so it is always "Open task"; otherwise the destination is derived
    /// from the corrected deep-link URL, falling back to the category, so the label always
    /// describes where the row actually goes.
    /// </summary>
    public static string ResolveActionLabel(Guid? taskId, string? deepLinkUrl, string category, string? actionTitle = null)
    {
        if (taskId is not null)
        {
            // Category/title-specific wording so the visible label/accessible name describes the
            // actual destination opened (the matching TaskViewDialog action panel), not just a
            // generic "Open task" — DSH ticket requirement that labels match their destination
            // exactly. ActionTitle disambiguates categories that cover more than one destination
            // (e.g. "Pending Sickness Actions" covers both return-to-work reviews and evidence
            // requests).
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

    /// <summary>Converts one server-returned action item into the shared row view model.</summary>
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

    /// <summary>
    /// Converts a full dashboard summary response into ordered outcomes + attention items,
    /// preserving each category's Required/Failed/ActionableCount for <see cref="WidgetPanelState"/>
    /// and skipping item extraction for failed categories.
    /// </summary>
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
