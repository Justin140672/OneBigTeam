namespace HR.Modules.Documents.Services;

internal sealed record VirusScanResult(bool IsClean, string? ThreatName = null)
{
    public static VirusScanResult Clean() => new(true);
    public static VirusScanResult Infected(string threatName) => new(false, threatName);
}
