using HR.Infrastructure.Abstractions;

namespace HR.Infrastructure.Tests;

/// <summary>
/// Ticket 4 (third hardening pass): the write-through counting stream that guards the per-export
/// temp-disk ceiling. It must refuse the offending write <b>before</b> a single byte reaches the inner
/// stream, enforce on every sync and async write path, allow the exact boundary, trip mid-sequence
/// once cumulative writes cross the ceiling, and never dispose the inner stream.
/// </summary>
public class OrganisationDataExportArchiveBudgetStreamTests
{
    private static (OrganisationDataExportArchiveBudgetStream Stream, TrackingStream Inner) New(long maxBytes)
    {
        var inner = new TrackingStream();
        return (new OrganisationDataExportArchiveBudgetStream(inner, maxBytes), inner);
    }

    [Fact]
    public void A_single_oversized_write_throws_and_writes_nothing_to_the_inner_stream()
    {
        var (stream, inner) = New(maxBytes: 10);

        var ex = Assert.Throws<OrganisationDataExportTempCapacityException>(
            () => stream.Write(new byte[11], 0, 11));

        Assert.Contains("per-export temp-disk ceiling", ex.Message);
        Assert.Equal(0, inner.Length);
        Assert.Equal(0, inner.Position);
        Assert.Equal(0, inner.WriteCallCount);
    }

    [Fact]
    public void Writes_within_budget_pass_through_byte_for_byte()
    {
        var (stream, inner) = New(maxBytes: 16);
        var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        stream.Write(payload, 0, payload.Length);
        stream.Write(payload, 0, payload.Length);

        Assert.Equal(payload.Concat(payload).ToArray(), inner.ToArray());
    }

    [Fact]
    public void The_write_that_lands_exactly_on_the_ceiling_is_allowed_one_byte_over_throws()
    {
        var (stream, inner) = New(maxBytes: 8);

        stream.Write(new byte[8], 0, 8); // exactly at the ceiling: allowed

        Assert.Equal(8, inner.Length);
        Assert.Throws<OrganisationDataExportTempCapacityException>(() => stream.WriteByte(0)); // 9th byte
        Assert.Equal(8, inner.Length);
    }

    [Fact]
    public void Enforcement_trips_mid_sequence_when_cumulative_writes_cross_the_ceiling()
    {
        var (stream, inner) = New(maxBytes: 10);

        stream.Write(new byte[4], 0, 4); // 4
        stream.Write(new byte[4], 0, 4); // 8
        var ex = Assert.Throws<OrganisationDataExportTempCapacityException>(
            () => stream.Write(new byte[4], 0, 4)); // would be 12

        Assert.Contains("per-export", ex.Message);
        Assert.Equal(8, inner.Length); // the crossing write left no trace
    }

    [Fact]
    public async Task Async_memory_write_path_enforces_the_ceiling()
    {
        var (stream, inner) = New(maxBytes: 10);

        await stream.WriteAsync(new byte[6].AsMemory());
        await Assert.ThrowsAsync<OrganisationDataExportTempCapacityException>(
            async () => await stream.WriteAsync(new byte[6].AsMemory()));

        Assert.Equal(6, inner.Length);
    }

    [Fact]
    public async Task Async_byte_array_write_path_enforces_the_ceiling()
    {
        var (stream, inner) = New(maxBytes: 10);

        await stream.WriteAsync(new byte[6], 0, 6);
        await Assert.ThrowsAsync<OrganisationDataExportTempCapacityException>(
            async () => await stream.WriteAsync(new byte[6], 0, 6));

        Assert.Equal(6, inner.Length);
    }

    [Fact]
    public void Span_write_path_enforces_the_ceiling()
    {
        var (stream, inner) = New(maxBytes: 4);

        Assert.Throws<OrganisationDataExportTempCapacityException>(
            () => stream.Write(new byte[5].AsSpan()));
        Assert.Equal(0, inner.Length);
    }

    [Fact]
    public void Dispose_does_not_dispose_the_inner_stream()
    {
        var (stream, inner) = New(maxBytes: 100);
        stream.Write(new byte[10], 0, 10);

        stream.Dispose();

        Assert.False(inner.IsDisposed);
        Assert.True(inner.CanWrite);
    }

    [Fact]
    public async Task DisposeAsync_does_not_dispose_the_inner_stream()
    {
        var (stream, inner) = New(maxBytes: 100);
        stream.Write(new byte[10], 0, 10);

        await stream.DisposeAsync();

        Assert.False(inner.IsDisposed);
        Assert.True(inner.CanWrite);
    }

    [Fact]
    public void High_water_mark_not_current_length_bounds_the_ceiling_after_a_rewind()
    {
        var (stream, _) = New(maxBytes: 8);
        stream.Write(new byte[8], 0, 8);

        // Rewind and overwrite: still within the high-water mark, still allowed.
        stream.Position = 0;
        stream.Write(new byte[8], 0, 8);

        // But growing past the high-water mark is refused.
        stream.Position = 8;
        Assert.Throws<OrganisationDataExportTempCapacityException>(() => stream.WriteByte(0));
    }

    private sealed class TrackingStream : MemoryStream
    {
        public int WriteCallCount { get; private set; }
        public bool IsDisposed { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteCallCount++;
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            WriteCallCount++;
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            WriteCallCount++;
            base.WriteByte(value);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            // Deliberately do not call base.Dispose so CanWrite stays observable for the assertion.
        }
    }
}
