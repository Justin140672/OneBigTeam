namespace HR.Web.Services;

/// <summary>
/// Client-side mirror of HR.Modules.Recruitment.Domain.ApplicationStatusTransitions. Used purely to
/// visually disable/reject invalid Kanban drag targets before a request is sent — the server
/// (MoveApplicationStage) remains the sole enforcement point and must be re-validated there.
/// Keep in sync with the backend graph if it ever changes.
/// </summary>
public static class ApplicationStatusTransitionRules
{
    // Pipeline order used for Kanban column layout — all eight statuses always present.
    public static readonly IReadOnlyList<string> ColumnOrder =
    [
        "Applied",
        "Screening",
        "InterviewScheduled",
        "Interviewed",
        "Offered",
        "Hired",
        "Rejected",
        "Withdrawn",
    ];

    private static readonly Dictionary<string, string[]> AllowedTransitions = new()
    {
        ["Applied"] = ["Screening", "InterviewScheduled", "Rejected", "Withdrawn"],
        ["Screening"] = ["InterviewScheduled", "Rejected", "Withdrawn"],
        ["InterviewScheduled"] = ["Interviewed", "Rejected", "Withdrawn"],
        ["Interviewed"] = ["Offered", "Rejected", "Withdrawn"],
        ["Offered"] = ["Hired", "Rejected", "Withdrawn"],
        ["Hired"] = [],
        ["Rejected"] = [],
        ["Withdrawn"] = [],
    };

    public static bool CanTransitionTo(string from, string to) =>
        from == to || (AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to));

    public static IReadOnlyCollection<string> GetAllowedNextStages(string from) =>
        AllowedTransitions.TryGetValue(from, out var allowed) ? allowed : [];
}
