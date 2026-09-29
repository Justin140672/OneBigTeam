namespace HR.Modules.Companies.Features.GetSystemHealth;

internal sealed record SystemHealthCategory(string Name, string Status, string? Description);

internal sealed record GetSystemHealthResponse(
    string OverallStatus,
    string PlatformVersion,
    DateTimeOffset CheckedAt,
    IReadOnlyList<SystemHealthCategory> Categories);
