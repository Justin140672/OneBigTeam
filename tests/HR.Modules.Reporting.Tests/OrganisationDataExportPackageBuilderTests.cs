using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests;

public class OrganisationDataExportPackageBuilderTests
{
    private readonly OrganisationDataExportPackageBuilder _builder = new();

    private static ZipArchive Open(byte[] bytes) => new(new MemoryStream(bytes), ZipArchiveMode.Read);

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        Assert.NotNull(entry);
        using var reader = new StreamReader(entry!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static async IAsyncEnumerable<DataExportTable> AsAsync(params DataExportTable[] tables)
    {
        foreach (var table in tables)
        {
            await Task.Yield();
            yield return table;
        }
    }

    /// <summary>Runs the streaming builder into an in-memory stream and returns the resulting archive bytes.</summary>
    private async Task<(byte[] Bytes, IReadOnlyList<string> Missing)> Build(
        IEnumerable<DataExportTable> tables,
        params (string ZipPath, byte[]? Content)[] files)
    {
        var entries = files.Select((f, i) => new DocumentExportFileEntry(f.ZipPath, $"key-{i}")).ToList();
        var contentByKey = entries
            .Select((e, i) => (e.StorageKey, files[i].Content))
            .ToDictionary(x => x.StorageKey, x => x.Content);

        var openCount = 0;
        var concurrentOpen = 0;
        var maxConcurrentOpen = 0;

        Task<Stream?> OpenDocument(DocumentExportFileEntry entry, CancellationToken ct)
        {
            openCount++;
            concurrentOpen++;
            maxConcurrentOpen = Math.Max(maxConcurrentOpen, concurrentOpen);
            var bytes = contentByKey[entry.StorageKey];
            Stream? stream = bytes is null ? null : new TrackingStream(bytes, () => concurrentOpen--);
            if (stream is null)
                concurrentOpen--;
            return Task.FromResult(stream);
        }

        using var output = new MemoryStream();
        var missing = await _builder.BuildToStreamAsync(
            AsAsyncEnumerable(tables), entries, OpenDocument, output, null, CancellationToken.None);

        LastMaxConcurrentOpen = maxConcurrentOpen;
        LastOpenCount = openCount;
        return (output.ToArray(), missing);
    }

    private int LastMaxConcurrentOpen;
    private int LastOpenCount;

    private static async IAsyncEnumerable<DataExportTable> AsAsyncEnumerable(IEnumerable<DataExportTable> tables)
    {
        foreach (var table in tables)
        {
            await Task.Yield();
            yield return table;
        }
    }

    [Fact]
    public async Task Build_Writes_One_Csv_Per_Table_With_Header_And_Crlf()
    {
        var table = new DataExportTable("employees",
            ["Id", "Name"],
            [new string?[] { "1", "Alice" }, new string?[] { "2", "Bob" }]);

        var (bytes, _) = await Build([table]);

        using var archive = Open(bytes);
        Assert.Equal("Id,Name\r\n1,Alice\r\n2,Bob\r\n", ReadEntry(archive, "employees.csv"));
    }

    [Fact]
    public async Task Build_Quotes_Fields_Containing_Comma_Quote_Or_Newline_And_Doubles_Quotes()
    {
        var table = new DataExportTable("t",
            ["A", "B", "C"],
            [new string?[] { "has,comma", "has\"quote", "line1\nline2" }]);

        var (bytes, _) = await Build([table]);

        using var archive = Open(bytes);
        Assert.Equal("A,B,C\r\n\"has,comma\",\"has\"\"quote\",\"line1\nline2\"\r\n", ReadEntry(archive, "t.csv"));
    }

    [Fact]
    public async Task Build_Renders_Null_Cells_As_Empty()
    {
        var table = new DataExportTable("t", ["A", "B"], [new string?[] { null, "x" }]);

        var (bytes, _) = await Build([table]);

        using var archive = Open(bytes);
        Assert.Equal("A,B\r\n,x\r\n", ReadEntry(archive, "t.csv"));
    }

    [Fact]
    public async Task Build_Adds_File_Entries_At_Their_Zip_Path()
    {
        var table = new DataExportTable("t", ["A"], []);

        var (bytes, missing) = await Build([table], ("documents/Contracts/offer.pdf", "PDF-BYTES"u8.ToArray()));

        Assert.Empty(missing);
        using var archive = Open(bytes);
        Assert.NotNull(archive.GetEntry("t.csv"));
        Assert.Equal("PDF-BYTES", ReadEntry(archive, "documents/Contracts/offer.pdf"));
    }

    [Fact]
    public async Task BuildToStream_Opens_Documents_One_At_A_Time_And_Disposes_Each()
    {
        var (_, missing) = await Build([],
            ("documents/x/a.pdf", "A"u8.ToArray()),
            ("documents/x/b.pdf", "B"u8.ToArray()),
            ("documents/x/c.pdf", "C"u8.ToArray()));

        Assert.Empty(missing);
        Assert.Equal(3, LastOpenCount);
        Assert.Equal(1, LastMaxConcurrentOpen);
    }

    [Fact]
    public async Task BuildToStream_Reports_Every_Missing_Document_And_Still_Opens_One_At_A_Time()
    {
        var (_, missing) = await Build([],
            ("documents/x/present.pdf", "OK"u8.ToArray()),
            ("documents/x/gone1.pdf", null),
            ("documents/x/gone2.pdf", null));

        Assert.Equal(new[] { "documents/x/gone1.pdf", "documents/x/gone2.pdf" }, missing.ToArray());
        Assert.Equal(1, LastMaxConcurrentOpen);
    }

    [Fact]
    public async Task BuildToStream_Invokes_The_Budget_Callback_After_Every_Entry()
    {
        var lengths = new List<long>();
        var entries = new List<DocumentExportFileEntry> { new("documents/x/a.pdf", "k0") };
        using var output = new MemoryStream();

        await _builder.BuildToStreamAsync(
            AsAsync(new DataExportTable("t", ["A"], [])),
            entries,
            (_, _) => Task.FromResult<Stream?>(new MemoryStream("A"u8.ToArray())),
            output,
            lengths.Add,
            CancellationToken.None);

        Assert.Equal(2, lengths.Count); // one table + one document
        Assert.True(lengths[1] >= lengths[0]);
    }

    [Fact]
    public async Task Build_Disambiguates_Two_File_Entries_With_The_Same_Zip_Path()
    {
        var (bytes, _) = await Build([],
            ("documents/Contracts/offer.pdf", "FIRST"u8.ToArray()),
            ("documents/Contracts/offer.pdf", "SECOND"u8.ToArray()));

        using var archive = Open(bytes);
        Assert.Equal("FIRST", ReadEntry(archive, "documents/Contracts/offer.pdf"));
        Assert.Equal("SECOND", ReadEntry(archive, "documents/Contracts/offer (2).pdf"));
    }

    [Fact]
    public async Task Build_Disambiguates_A_File_Entry_That_Collides_With_A_Table_Csv_Name()
    {
        var table = new DataExportTable("report", ["A"], [new string?[] { "row" }]);

        var (bytes, _) = await Build([table], ("report.csv", "FILE-BODY"u8.ToArray()));

        using var archive = Open(bytes);
        Assert.Equal("A\r\nrow\r\n", ReadEntry(archive, "report.csv"));
        Assert.Equal("FILE-BODY", ReadEntry(archive, "report (2).csv"));
    }

    [Fact]
    public async Task Build_Disambiguates_Three_Identical_Names_As_2_And_3()
    {
        var (bytes, _) = await Build([],
            ("documents/x/file.pdf", "A"u8.ToArray()),
            ("documents/x/file.pdf", "B"u8.ToArray()),
            ("documents/x/file.pdf", "C"u8.ToArray()));

        using var archive = Open(bytes);
        Assert.Equal("A", ReadEntry(archive, "documents/x/file.pdf"));
        Assert.Equal("B", ReadEntry(archive, "documents/x/file (2).pdf"));
        Assert.Equal("C", ReadEntry(archive, "documents/x/file (3).pdf"));
    }

    // ----- Ticket 4 follow-up: streamed (DataExportTable.Streamed) tables -----

    private static DataExportTable Streamed(
        string name, IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<string?>> rows) =>
        DataExportTable.Streamed(name, columns, _ => ToAsync(rows));

    private static async IAsyncEnumerable<IReadOnlyList<string?>> ToAsync(IEnumerable<IReadOnlyList<string?>> rows)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            yield return row;
        }
    }

    [Fact]
    public async Task Streamed_Table_Produces_Byte_Identical_Csv_To_The_Equivalent_Eager_Table()
    {
        IReadOnlyList<string> columns = ["Id", "Name", "Note"];
        var data = new IReadOnlyList<string?>[]
        {
            new string?[] { "1", "Alice", null },
            new string?[] { "2", "Bob", "plain" },
            new string?[] { "3", "Eve", "has,comma" },
        };

        var (eagerBytes, _) = await Build([new DataExportTable("t", columns, data)]);
        var (streamedBytes, _) = await Build([Streamed("t", columns, data)]);

        using var eager = Open(eagerBytes);
        using var streamed = Open(streamedBytes);
        Assert.Equal(ReadEntry(eager, "t.csv"), ReadEntry(streamed, "t.csv"));
    }

    [Fact]
    public async Task Streamed_Rows_Are_Rfc4180_Escaped_Exactly_Like_Eager_Rows()
    {
        var table = Streamed("t", ["A", "B", "C"],
            [new string?[] { "has,comma", "has\"quote", "line1\nline2" }]);

        var (bytes, _) = await Build([table]);

        using var archive = Open(bytes);
        Assert.Equal("A,B,C\r\n\"has,comma\",\"has\"\"quote\",\"line1\nline2\"\r\n", ReadEntry(archive, "t.csv"));
    }

    [Fact]
    public async Task Large_Streamed_Row_Set_Is_Written_Without_Ever_Materialising_All_Rows()
    {
        const int rowCount = 5_000;
        var live = 0;
        var maxLive = 0;
        var yielded = 0;

        async IAsyncEnumerable<IReadOnlyList<string?>> Generator(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 0; i < rowCount; i++)
            {
                ct.ThrowIfCancellationRequested();
                var now = Interlocked.Increment(ref live);
                maxLive = Math.Max(maxLive, now);
                yield return new string?[] { i.ToString(), $"row-{i}" };
                Interlocked.Increment(ref yielded);
                Interlocked.Decrement(ref live);
                await Task.Yield();
            }
        }

        var table = DataExportTable.Streamed("big", ["Id", "Name"], Generator);

        using var output = new MemoryStream();
        await _builder.BuildToStreamAsync(
            AsAsyncEnumerable([table]),
            [],
            (_, _) => Task.FromResult<Stream?>(null),
            output,
            null,
            CancellationToken.None);

        Assert.Equal(rowCount, yielded);

        // Behavioural: the builder pulls the generator one row at a time — it never asks for all
        // rows up front. This is NOT a retained-memory guarantee on its own (a consumer could still
        // buffer every row it is handed); the retained-bytes proof lives in the parked-generator
        // tests below, which measure what actually reaches the output stream mid-enumeration.
        Assert.True(maxLive <= 2, $"builder requested rows ahead of writing them: max concurrently-live was {maxLive}");

        using var archive = Open(output.ToArray());
        var lines = ReadEntry(archive, "big.csv").Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(rowCount + 1, lines.Length); // header + every row
    }

    [Fact]
    public async Task Streamed_Table_Yields_One_Csv_Entry_Named_After_The_Table()
    {
        var (bytes, _) = await Build([Streamed("audit_log", ["A"], [new string?[] { "x" }])]);

        using var archive = Open(bytes);
        Assert.Equal(new[] { "audit_log.csv" }, archive.Entries.Select(e => e.FullName).ToArray());
        Assert.Equal("A\r\nx\r\n", ReadEntry(archive, "audit_log.csv"));
    }

    [Fact]
    public async Task Streamed_Table_Name_Colliding_With_Another_Table_Is_Uniquified()
    {
        var (bytes, _) = await Build(
        [
            Streamed("audit_log", ["A"], [new string?[] { "first" }]),
            Streamed("audit_log", ["A"], [new string?[] { "second" }]),
        ]);

        using var archive = Open(bytes);
        Assert.Equal("A\r\nfirst\r\n", ReadEntry(archive, "audit_log.csv"));
        Assert.Equal("A\r\nsecond\r\n", ReadEntry(archive, "audit_log (2).csv"));
    }

    [Fact]
    public async Task Documents_Are_Still_Embedded_Alongside_A_Streamed_Table()
    {
        var (bytes, missing) = await Build(
            [Streamed("audit_log", ["A"], [new string?[] { "x" }])],
            ("documents/Contracts/offer.pdf", "PDF-BYTES"u8.ToArray()),
            ("documents/Contracts/offer.pdf", "PDF-BYTES-2"u8.ToArray()));

        Assert.Empty(missing);
        using var archive = Open(bytes);
        Assert.Equal("A\r\nx\r\n", ReadEntry(archive, "audit_log.csv"));
        Assert.Equal("PDF-BYTES", ReadEntry(archive, "documents/Contracts/offer.pdf"));
        Assert.Equal("PDF-BYTES-2", ReadEntry(archive, "documents/Contracts/offer (2).pdf"));
    }

    [Fact]
    public async Task Cancellation_Mid_Enumeration_Of_A_Streamed_Table_Propagates_And_Stops_The_Build()
    {
        using var cts = new CancellationTokenSource();
        var emitted = 0;

        async IAsyncEnumerable<IReadOnlyList<string?>> Generator(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 0; ; i++)
            {
                ct.ThrowIfCancellationRequested();
                yield return new string?[] { i.ToString() };
                emitted++;
                cts.Cancel(); // cancel right after the first row
                await Task.Yield();
            }
        }

        var table = DataExportTable.Streamed("audit_log", ["A"], Generator);

        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _builder.BuildToStreamAsync(
                AsAsyncEnumerable([table]),
                [],
                (_, _) => Task.FromResult<Stream?>(null),
                output,
                null,
                cts.Token));

        Assert.Equal(1, emitted);
    }

    [Fact]
    public async Task Cancellation_Mid_Enumeration_Of_An_Eager_Rows_Table_Propagates_And_Stops_The_Build()
    {
        // Mirror of Cancellation_Mid_Enumeration_Of_A_Streamed_Table for the eager Rows path: the
        // builder must check the token inside the eager loop, not only the streamed loop.
        using var cts = new CancellationTokenSource();

        // A lazy eager-Rows collection that cancels the token the moment the builder asks for the
        // second row — so the per-row token check inside the eager loop is what must stop the build.
        var enumerated = 0;
        IEnumerable<IReadOnlyList<string?>> RowSource()
        {
            for (var i = 0; i < 5_000; i++)
            {
                enumerated++;
                if (i == 1)
                    cts.Cancel();
                yield return new string?[] { i.ToString(), $"row-{i}" };
            }
        }

        var table = new DataExportTable("big", ["Id", "Name"], new RowList(RowSource()));
        var output = new CallbackStream(() => { });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _builder.BuildToStreamAsync(
                AsAsync(table), [], (_, _) => Task.FromResult<Stream?>(null), output, null, cts.Token));

        // The builder stopped almost immediately instead of draining all 5,000 rows.
        Assert.True(enumerated < 100, $"builder enumerated {enumerated} rows after cancellation");
    }

    // ----- Ticket 4 final follow-up (Finding 3): retained-bytes, not just BytesWritten > 0 -----
    //
    // ZIP local-file headers make output.BytesWritten > 0 before a single row's data is written, so
    // "> 0" is satisfied even by a consumer that buffers the whole stream. These tests instead capture
    // a BASELINE at the first row and require a substantial number of bytes (>> any header/buffer) to
    // reach the output while the generator is still parked / mid-enumeration.

    private const long RetainedBytesThreshold = 64 * 1024;

    [Fact]
    public async Task A_Substantial_Volume_Of_Row_Data_Reaches_The_Output_While_The_Generator_Is_Parked_Not_Just_Zip_Headers()
    {
        var reachedGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        long baselineAtFirstRow = -1;
        var parkedAfter = 0;
        CallbackStream? output = null;

        async IAsyncEnumerable<IReadOnlyList<string?>> Generator(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 0; i < 20_000; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i == 0)
                    baselineAtFirstRow = output!.BytesWritten; // whatever is written so far == ZIP header only

                // Poorly-compressible prefix: three "N"-format Guids per row (~96 chars), well past
                // any StreamWriter / deflate buffer once accumulated over thousands of rows.
                yield return new string?[]
                {
                    i.ToString(),
                    string.Concat(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")),
                };
            }

            parkedAfter = 20_000;
            reachedGate.SetResult();
            await releaseGate.Task;

            yield return new string?[] { "last", "row" };
        }

        var table = DataExportTable.Streamed("big", ["Id", "Blob"], Generator);
        output = new CallbackStream(() => { });

        var build = _builder.BuildToStreamAsync(
            AsAsync(table), [], (_, _) => Task.FromResult<Stream?>(null), output, null, CancellationToken.None);

        try
        {
            await reachedGate.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(build.IsCompleted, "the build completed before the generator was released");
            Assert.Equal(20_000, parkedAfter);
            Assert.True(baselineAtFirstRow >= 0, "the generator never produced its first row");

            var grownWhileParked = output.BytesWritten - baselineAtFirstRow;
            Assert.True(grownWhileParked >= RetainedBytesThreshold,
                $"only {grownWhileParked} bytes beyond the first-row baseline reached the output while the " +
                $"generator was parked — a whole-stream buffering consumer would sit near 0 here " +
                $"(expected >= {RetainedBytesThreshold})");
        }
        finally
        {
            releaseGate.TrySetResult();
            await build.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Output_Byte_Count_Actually_Increases_Across_Streamed_Enumeration_Not_Merely_Stays_Nonzero()
    {
        CallbackStream? output = null;
        var checkpoints = new List<long>();
        const int rows = 30_000;
        const int step = 5_000;

        async IAsyncEnumerable<IReadOnlyList<string?>> Generator(
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 0; i < rows; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i % step == 0 && output is not null)
                    checkpoints.Add(output.BytesWritten);

                yield return new string?[]
                {
                    i.ToString(),
                    string.Concat(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N")),
                };
            }
        }

        var table = DataExportTable.Streamed("big", ["Id", "Blob"], Generator);
        output = new CallbackStream(() => { });

        await _builder.BuildToStreamAsync(
            AsAsync(table), [], (_, _) => Task.FromResult<Stream?>(null), output, null, CancellationToken.None);

        // Several checkpoints captured mid-enumeration must show real, substantial growth over time —
        // not merely "non-zero" and not merely "non-decreasing". A whole-stream buffering consumer
        // would leave every checkpoint pinned near 0.
        Assert.True(checkpoints.Count >= 4, $"expected several mid-enumeration checkpoints, got {checkpoints.Count}");
        for (var i = 1; i < checkpoints.Count; i++)
        {
            Assert.True(checkpoints[i] > checkpoints[i - 1],
                $"checkpoint {i} ({checkpoints[i]} bytes) did not grow past checkpoint {i - 1} " +
                $"({checkpoints[i - 1]} bytes) — output is not being streamed as rows arrive");
        }

        Assert.True(checkpoints[^1] - checkpoints[0] >= RetainedBytesThreshold,
            $"output grew only {checkpoints[^1] - checkpoints[0]} bytes across enumeration (expected >= {RetainedBytesThreshold})");
        Assert.True(output.BytesWritten >= checkpoints[^1]);
    }

    /// <summary>
    /// Lazy <see cref="IReadOnlyList{T}"/> facade over an <see cref="IEnumerable{T}"/> — lets a test
    /// drive side effects (e.g. cancelling a token) from inside the builder's eager <c>Rows</c> loop.
    /// Only <see cref="GetEnumerator"/> is exercised by the builder.
    /// </summary>
    private sealed class RowList(IEnumerable<IReadOnlyList<string?>> source) : IReadOnlyList<IReadOnlyList<string?>>
    {
        public IEnumerator<IReadOnlyList<string?>> GetEnumerator() => source.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public int Count => throw new NotSupportedException();
        public IReadOnlyList<string?> this[int index] => throw new NotSupportedException();
    }

    /// <summary>Write-only stream that counts bytes and fires a callback on every write.</summary>
    private sealed class CallbackStream(Action onWrite) : Stream
    {
        private long _bytes;

        public long BytesWritten => Interlocked.Read(ref _bytes);

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Interlocked.Read(ref _bytes);
        public override long Position { get => Interlocked.Read(ref _bytes); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Interlocked.Add(ref _bytes, count);
            onWrite();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Interlocked.Add(ref _bytes, buffer.Length);
            onWrite();
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Add(ref _bytes, count);
            onWrite();
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Add(ref _bytes, buffer.Length);
            onWrite();
            return ValueTask.CompletedTask;
        }

        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class TrackingStream(byte[] bytes, Action onDispose) : MemoryStream(bytes, writable: false)
    {
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                onDispose();
            base.Dispose(disposing);
        }
    }
}
