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
            "Generate and improve job descriptions and position profiles using AI.",
            UpcomingFeatureStatus.ComingSoon),
        new UpcomingFeatureInfo(
            "diagram-project",
            "Employee webhooks",
            "Integrate One Big Team with other systems when employees join, change or leave.",
            UpcomingFeatureStatus.ComingSoon),
        new UpcomingFeatureInfo(
            "chart-line",
            "AI help assistant",
            "Ask questions about One Big Team and get contextual help.",
            UpcomingFeatureStatus.Planned),
    };
}
