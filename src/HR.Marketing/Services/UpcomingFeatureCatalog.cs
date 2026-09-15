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
    public static readonly IReadOnlyList<UpcomingFeatureInfo> All =
        HR.SharedKernel.PhaseTwoRoadmapCatalog.All
            .Select(item => new UpcomingFeatureInfo(item.IconName, item.Title, item.Description,
                UpcomingFeatureStatus.ComingSoon))
            .ToList().AsReadOnly();
}
