namespace HR.Marketing.Services;

public enum UpcomingFeatureStatus
{
    ComingSoon,
    Planned,
    Exploring
}

public sealed record UpcomingFeatureInfo(
    string IconName,
    string Title,
    string Description,
    UpcomingFeatureStatus Status);

public static class UpcomingFeatureCatalog
{
    public static readonly IReadOnlyList<UpcomingFeatureInfo> All = new[]
    {
        new UpcomingFeatureInfo(
            "folder-open",
            "AI-powered position profiles",
            "Writing a good job description or position profile from scratch takes time you probably don't have. AI-assisted drafting will help you get to a strong, ready-to-use position profile faster, so you can focus on hiring rather than wordsmithing.",
            UpcomingFeatureStatus.ComingSoon),
        new UpcomingFeatureInfo(
            "diagram-project",
            "Employee webhooks",
            "Re-keying the same joiner, mover and leaver updates into other systems is slow and error-prone. Webhooks will let One Big Team notify the other tools you use automatically when an employee joins, changes role or leaves, keeping everything in sync without manual double-entry.",
            UpcomingFeatureStatus.ComingSoon),
        new UpcomingFeatureInfo(
            "chart-line",
            "AI help assistant",
            "Not everyone wants to dig through help articles to find an answer. A built-in AI assistant will let you ask a question in plain language and get a contextual answer right where you're working, so you can get back to the task at hand.",
            UpcomingFeatureStatus.Planned),
    };
}
