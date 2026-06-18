using HR.Modules.Documents.Services;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class FakeVirusScanService : IVirusScanService
{
    public bool ReturnInfected { get; set; }
    public string ThreatName { get; set; } = "EICAR.Test.File";

    public Task<VirusScanResult> ScanAsync(
        Stream content,
        string fileName,
        CancellationToken cancellationToken)
        => Task.FromResult(ReturnInfected
            ? VirusScanResult.Infected(ThreatName)
            : VirusScanResult.Clean());
}
