using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Jobs;

/// <summary>
/// Ticket 4: the process-wide concurrency cap on organisation data export builds. Uses a tiny
/// SlotAcquireTimeout so the "no slot free" path resolves in milliseconds and the suite stays
/// deterministic at high parallelism. No shared static state.
/// </summary>
public sealed class OrganisationDataExportConcurrencyGateTests
{
    private static OrganisationDataExportConcurrencyGate Gate(int max, int timeoutMs = 50) =>
        new(new OrganisationDataExportResourceLimits
        {
            MaxConcurrentExports = max,
            SlotAcquireTimeout = TimeSpan.FromMilliseconds(timeoutMs),
        });

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 5)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    public void MaxConcurrentExports_Reflects_The_Limit_With_A_Floor_Of_One(int configured, int expected)
    {
        Assert.Equal(expected, Gate(configured).MaxConcurrentExports);
    }

    [Fact]
    public async Task AcquireAsync_Grants_Slots_Up_To_The_Limit_Then_Returns_Null_After_The_Timeout()
    {
        var gate = Gate(max: 2);

        var first = await gate.AcquireAsync(CancellationToken.None);
        var second = await gate.AcquireAsync(CancellationToken.None);
        Assert.NotNull(first);
        Assert.NotNull(second);

        var third = await gate.AcquireAsync(CancellationToken.None);
        Assert.Null(third);

        // Releasing one slot lets a further acquisition succeed.
        await first!.DisposeAsync();
        var fourth = await gate.AcquireAsync(CancellationToken.None);
        Assert.NotNull(fourth);

        await second!.DisposeAsync();
        await fourth!.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_A_Handle_Twice_Only_Releases_One_Slot()
    {
        var gate = Gate(max: 2);

        var a = await gate.AcquireAsync(CancellationToken.None);
        var b = await gate.AcquireAsync(CancellationToken.None);
        Assert.NotNull(a);
        Assert.NotNull(b);

        await a!.DisposeAsync();
        await a.DisposeAsync(); // second dispose must be a no-op

        // Exactly one slot freed: one acquire succeeds, the next times out.
        var c = await gate.AcquireAsync(CancellationToken.None);
        Assert.NotNull(c);

        var d = await gate.AcquireAsync(CancellationToken.None);
        Assert.Null(d);

        await b!.DisposeAsync();
        await c!.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_Honours_A_Cancelled_Token()
    {
        var gate = Gate(max: 1, timeoutMs: 10_000);
        var held = await gate.AcquireAsync(CancellationToken.None);
        Assert.NotNull(held);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => gate.AcquireAsync(cts.Token));

        await held!.DisposeAsync();
    }
}
