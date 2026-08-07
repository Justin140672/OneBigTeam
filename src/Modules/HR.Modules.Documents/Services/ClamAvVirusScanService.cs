using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Real virus scanner backed by a clamd (ClamAV daemon) instance, speaking the INSTREAM protocol
/// directly over TCP — the file's bytes are streamed to clamd in length-prefixed chunks with no
/// temp file ever written to disk, matching the ticket's "no temp files unless truly unavoidable"
/// requirement.
///
/// INSTREAM wire format (see https://docs.clamav.net/manual/Usage/Scanning.html#instream):
///   1. Send "zINSTREAM\0" (the leading 'z' means the command itself is NUL-terminated).
///   2. Send the file in chunks, each chunk prefixed with its 4-byte big-endian length.
///   3. Send a zero-length chunk (4 zero bytes) to signal end of stream.
///   4. Read clamd's reply line — "stream: OK" (clean) or "stream: <name> FOUND" (infected), or
///      an error string.
/// </summary>
internal sealed class ClamAvVirusScanService(IOptions<ClamAvOptions> options) : IVirusScanService
{
    private const int ChunkSize = 8192;

    public async Task<VirusScanResult> ScanAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        var settings = options.Value;

        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

        await client.ConnectAsync(settings.Host, settings.Port, timeoutCts.Token);

        await using var networkStream = client.GetStream();

        var command = Encoding.ASCII.GetBytes("zINSTREAM\0");
        await networkStream.WriteAsync(command, timeoutCts.Token);

        var buffer = new byte[ChunkSize];
        int bytesRead;
        while ((bytesRead = await content.ReadAsync(buffer.AsMemory(0, ChunkSize), timeoutCts.Token)) > 0)
        {
            var lengthPrefix = BitConverter.GetBytes(bytesRead);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(lengthPrefix);

            await networkStream.WriteAsync(lengthPrefix, timeoutCts.Token);
            await networkStream.WriteAsync(buffer.AsMemory(0, bytesRead), timeoutCts.Token);
        }

        // Zero-length chunk terminates the stream.
        var zeroLength = new byte[4];
        await networkStream.WriteAsync(zeroLength, timeoutCts.Token);

        using var reader = new StreamReader(networkStream, Encoding.ASCII, leaveOpen: true);
        var reply = await reader.ReadLineAsync(timeoutCts.Token) ?? string.Empty;

        if (reply.Contains("FOUND", StringComparison.Ordinal))
        {
            // Reply format: "stream: <threat-name> FOUND"
            var threatName = reply
                .Replace("stream:", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("FOUND", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            return VirusScanResult.Infected(string.IsNullOrWhiteSpace(threatName) ? "Unknown threat" : threatName);
        }

        if (reply.Contains("OK", StringComparison.Ordinal))
            return VirusScanResult.Clean();

        // Any other reply (ERROR, connection reset mid-scan, etc.) is treated as an unreachable/
        // errored scanner — the caller (ScanUploadedFileJob) lets Hangfire's automatic retry
        // handle this rather than treating it as "infected".
        throw new InvalidOperationException($"Unexpected clamd response scanning '{fileName}': '{reply}'.");
    }
}
