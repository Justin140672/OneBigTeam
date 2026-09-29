using System.Text;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal static class EicarTestFile
{
    public const string Signature = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    public const string ThreatName = "Eicar-Test-Signature";

    public static byte[] Bytes => Encoding.ASCII.GetBytes(Signature);
}

internal sealed class EicarDetectingUploadedFileScanner : IUploadedFileScanner
{
    public List<(string FileName, byte[] Content)> Scans { get; } = [];

    public async Task<UploadedFileScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        var bytes = buffer.ToArray();
        Scans.Add((fileName, bytes));

        return Encoding.ASCII.GetString(bytes).Contains(EicarTestFile.Signature, StringComparison.Ordinal)
            ? UploadedFileScanResult.Infected(EicarTestFile.ThreatName)
            : UploadedFileScanResult.Clean();
    }
}

internal sealed class FixedVerdictUploadedFileScanner(UploadedFileScanResult verdict) : IUploadedFileScanner
{
    public int Calls { get; private set; }

    public Task<UploadedFileScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(verdict);
    }
}

internal sealed class ThrowingUploadedFileScanner(Func<Exception> exceptionFactory) : IUploadedFileScanner
{
    public int Calls { get; private set; }

    public Task<UploadedFileScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        Calls++;
        throw exceptionFactory();
    }
}

internal sealed class ThrowingBackgroundJobClient : IBackgroundJobClient
{
    public int Attempts { get; private set; }

    public string Create(Job job, IState state)
    {
        Attempts++;
        throw new InvalidOperationException("Simulated Hangfire storage outage.");
    }

    public bool ChangeState(string jobId, IState state, string? expectedState) =>
        throw new InvalidOperationException("Simulated Hangfire storage outage.");
}
