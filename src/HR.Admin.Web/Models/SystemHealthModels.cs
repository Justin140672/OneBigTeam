namespace HR.Admin.Web.Models;

public sealed record SystemHealthResponse(
    string OverallStatus,
    string PlatformVersion,
    DateTimeOffset CheckedAt,
    IReadOnlyList<SystemHealthCategory> Categories);

public sealed record SystemHealthCategory(string Name, string Status, string? Description);
