using System.Net;
using System.Net.Sockets;
using System.Text;
using HR.Modules.Documents.Services;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Tests;

/// <summary>
/// TEST-004 — ClamAV INSTREAM adapter. <see cref="ClamAvVirusScanService"/> talks raw TCP to clamd
/// and has no injectable socket seam, so these tests stand up a minimal in-process fake clamd on a
/// loopback port and assert the reply-line -> <see cref="VirusScanResult"/> mapping:
/// clean / infected / malformed / unreachable. Critically, an "infected" or unrecognised reply can
/// NEVER be reported as clean.
/// </summary>
public class ClamAvVirusScanServiceTests
{
    /// <summary>Accepts a single connection, consumes the INSTREAM bytes and writes back a fixed
    /// reply line. If <paramref name="reply"/> is null the connection is accepted then dropped
    /// (simulates clamd closing mid-scan).</summary>
    private sealed class FakeClamd : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly Task _loop;

        private FakeClamd(TcpListener listener, string? reply)
        {
            _listener = listener;
            _loop = RunAsync(reply);
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public static FakeClamd Start(string? reply)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new FakeClamd(listener, reply);
        }

        private async Task RunAsync(string? reply)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();

                // Drain whatever the client sends for a short window (command + chunks + terminator).
                var buffer = new byte[4096];
                client.ReceiveTimeout = 200;
                var readUntil = DateTime.UtcNow.AddMilliseconds(150);
                while (DateTime.UtcNow < readUntil)
                {
                    using var readCts = new CancellationTokenSource(50);
                    try
                    {
                        var n = await stream.ReadAsync(buffer, readCts.Token);
                        if (n == 0) break;
                    }
                    catch (OperationCanceledException) { }
                }

                if (reply is null)
                    return; // drop the connection without replying

                var bytes = Encoding.ASCII.GetBytes(reply + "\n");
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }
            catch
            {
                // best effort — the test asserts on the client side
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { _listener.Stop(); } catch { }
            try { await _loop; } catch { }
        }
    }

    private static ClamAvVirusScanService Build(int port) =>
        new(Options.Create(new ClamAvOptions { Host = "127.0.0.1", Port = port, TimeoutSeconds = 5 }));

    private static MemoryStream File() => new(Encoding.UTF8.GetBytes("some file contents to scan"));

    [Fact]
    public async Task ScanAsync_Clean_Reply_Maps_To_Clean()
    {
        await using var clamd = FakeClamd.Start("stream: OK");
        var result = await Build(clamd.Port).ScanAsync(File(), "doc.pdf", CancellationToken.None);

        Assert.True(result.IsClean);
        Assert.Null(result.ThreatName);
    }

    [Fact]
    public async Task ScanAsync_Infected_Reply_Maps_To_Infected_With_Threat_Name_Never_Clean()
    {
        await using var clamd = FakeClamd.Start("stream: Win.Test.EICAR_HDB-1 FOUND");
        var result = await Build(clamd.Port).ScanAsync(File(), "evil.exe", CancellationToken.None);

        Assert.False(result.IsClean);
        Assert.Equal("Win.Test.EICAR_HDB-1", result.ThreatName);
    }

    [Fact]
    public async Task ScanAsync_Reply_Containing_Both_FOUND_And_OK_Is_Treated_As_Infected()
    {
        // Defence against a reply like "stream: OK-ish.Thing FOUND" being misread as clean.
        await using var clamd = FakeClamd.Start("stream: Some.OK.Named.Thing FOUND");
        var result = await Build(clamd.Port).ScanAsync(File(), "x", CancellationToken.None);

        Assert.False(result.IsClean);
    }

    [Fact]
    public async Task ScanAsync_Infected_Reply_Without_Threat_Name_Still_Infected()
    {
        await using var clamd = FakeClamd.Start("stream: FOUND");
        var result = await Build(clamd.Port).ScanAsync(File(), "x", CancellationToken.None);

        Assert.False(result.IsClean);
        Assert.Equal("Unknown threat", result.ThreatName);
    }

    [Fact]
    public async Task ScanAsync_Malformed_Reply_Throws_And_Never_Reports_Clean()
    {
        await using var clamd = FakeClamd.Start("GIBBERISH ERROR: size limit exceeded");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(clamd.Port).ScanAsync(File(), "doc.pdf", CancellationToken.None));

        Assert.Contains("Unexpected clamd response", ex.Message);
    }

    [Fact]
    public async Task ScanAsync_Empty_Reply_Throws()
    {
        await using var clamd = FakeClamd.Start(string.Empty);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Build(clamd.Port).ScanAsync(File(), "doc.pdf", CancellationToken.None));
    }

    [Fact]
    public async Task ScanAsync_Connection_Dropped_Before_Reply_Throws_Not_Clean()
    {
        await using var clamd = FakeClamd.Start(reply: null);
        await Assert.ThrowsAnyAsync<Exception>(
            () => Build(clamd.Port).ScanAsync(File(), "doc.pdf", CancellationToken.None));
    }

    [Fact]
    public async Task ScanAsync_Scanner_Unreachable_Throws_Not_Clean()
    {
        // Nothing listening on this port.
        var freePort = GetUnusedPort();
        await Assert.ThrowsAnyAsync<Exception>(
            () => Build(freePort).ScanAsync(File(), "doc.pdf", CancellationToken.None));
    }

    [Fact]
    public async Task ScanAsync_Honours_Cancellation_Token()
    {
        var freePort = GetUnusedPort();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Build(freePort).ScanAsync(File(), "doc.pdf", cts.Token));
    }

    private static int GetUnusedPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
