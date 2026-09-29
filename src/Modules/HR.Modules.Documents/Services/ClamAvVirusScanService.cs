using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Services;

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

        var zeroLength = new byte[4];
        await networkStream.WriteAsync(zeroLength, timeoutCts.Token);

        using var reader = new StreamReader(networkStream, Encoding.ASCII, leaveOpen: true);
        var reply = await reader.ReadLineAsync(timeoutCts.Token) ?? string.Empty;

        if (reply.Contains("FOUND", StringComparison.Ordinal))
        {
            var threatName = reply
                .Replace("stream:", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("FOUND", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim();

            return VirusScanResult.Infected(string.IsNullOrWhiteSpace(threatName) ? "Unknown threat" : threatName);
        }

        if (reply.Contains("OK", StringComparison.Ordinal))
            return VirusScanResult.Clean();

        throw new InvalidOperationException($"Unexpected clamd response scanning '{fileName}': '{reply}'.");
    }
}
