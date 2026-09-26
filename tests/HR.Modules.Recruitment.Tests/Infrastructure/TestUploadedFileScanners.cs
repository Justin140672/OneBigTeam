using System.Text;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

/// <summary>
/// [P1] Candidate CV malware scanning test doubles for <see cref="IUploadedFileScanner"/>.
/// </summary>
internal static class EicarTestFile
{
    /// <summary>The industry-standard, harmless EICAR anti-virus test string.</summary>
    public const string Signature = @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*";

    public const string ThreatName = "Eicar-Test-Signature";

    public static byte[] Bytes => Encoding.ASCII.GetBytes(Signature);
}

/// <summary>
/// Content-inspecting scanner: reports Infected("Eicar-Test-Signature") when the stream's bytes
/// contain the EICAR signature, otherwise Clean. Ignores the file name entirely, so it proves the
/// verdict is driven by stored bytes rather than declared extension/content type. Records every
/// call so tests can assert what was scanned.
/// </summary>
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

/// <summary>Always returns the configured verdict, regardless of content.</summary>
internal sealed class FixedVerdictUploadedFileScanner(UploadedFileScanResult verdict) : IUploadedFileScanner
{
    public int Calls { get; private set; }

    public Task<UploadedFileScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(verdict);
    }
}

/// <summary>Simulates a scanner outage by throwing the supplied exception on every call.</summary>
internal sealed class ThrowingUploadedFileScanner(Func<Exception> exceptionFactory) : IUploadedFileScanner
{
    public int Calls { get; private set; }

    public Task<UploadedFileScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        Calls++;
        throw exceptionFactory();
    }
}

/// <summary>An <see cref="IBackgroundJobClient"/> whose job store is unavailable.</summary>
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
