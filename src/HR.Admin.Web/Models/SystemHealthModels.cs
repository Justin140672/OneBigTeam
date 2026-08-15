namespace HR.Admin.Web.Models;

// Mirrors HR.Modules.Companies.Features.GetSystemHealth.Response's shape exactly — same
// "app-local DTO matching the API contract" convention as CustomerDashboardModels etc.
public sealed record SystemHealthResponse(
    string OverallStatus,
    string PlatformVersion,
    DateTimeOffset CheckedAt,
    IReadOnlyList<SystemHealthCategory> Categories);

public sealed record SystemHealthCategory(string Name, string Status, string? Description);
