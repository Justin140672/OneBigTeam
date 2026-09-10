using System.IO.Compression;
using System.Runtime.CompilerServices;
using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Jobs;

/// <summary>
/// Ticket 4 (third hardening pass): the package builder writing through
/// <see cref="IOrganisationDataExportWorkspace.CreateBudgetEnforcingWriteStream"/> into the real
/// workspace archive <see cref="FileStream"/> must trip the per-export ceiling <b>mid-entry</b> and
/// <b>during ZIP finalisation</b>, and the archive file on disk must never exceed the ceiling by more
/// than the aborted write. Each test gets a unique work root so the suite is deterministic at high
/// parallelism.
/// </summary>
public sealed class OrganisationDataExportBudgetedBuildTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "obt-test-budgeted-build", Guid.NewGuid().ToString("N"));

    private readonly OrganisationDataExportPackageBuilder _builder = new();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort test cleanup
        }
    }

    private OrganisationDataExportWorkspaceFactory Factory(long maxArchive) =>
        new(new OrganisationDataExportResourceLimits
        {
            MaxArchiveBytesPerExport = maxArchive,
            MaxTotalWorkspaceBytes = long.MaxValue,
            MinimumFreeDiskBytes = 0,
        }, rootOverride: _root);

    private static async IAsyncEnumerable<DataExportTable> One(DataExportTable table)
    {
        await Task.Yield();
        yield return table;
    }

    /// <summary>
    /// A streamed table whose blob column is poorly compressible (random hex) so ZIP deflate cannot
    /// shrink it below the ceiling. When <paramref name="seed"/> is supplied the content is
    /// deterministic and byte-identical on every enumeration, so a "measure then re-run just under the
    /// measured size" test is stable; otherwise each enumeration is fresh randomness.
    /// </summary>
    private static DataExportTable HighEntropyStreamedTable(string name, int rows, int? seed = null)
    {
        async IAsyncEnumerable<IReadOnlyList<string?>> Gen([EnumeratorCancellation] CancellationToken ct = default)
        {
            var rng = seed is { } s ? new Random(s) : null;
            for (var i = 0; i < rows; i++)
            {
                ct.ThrowIfCancellationRequested();
                string blob;
                if (rng is null)
                {
                    blob = string.Concat(Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
                }
                else
                {
                    var buf = new byte[48];
                    rng.NextBytes(buf);
                    blob = Convert.ToHexString(buf);
                }

                yield return new string?[] { i.ToString(), blob };
                await Task.Yield();
            }
        }

        return DataExportTable.Streamed(name, ["Id", "Blob"], Gen);
    }

    [Fact]
    public async Task A_Streamed_Table_Larger_Than_The_Ceiling_Trips_Mid_Entry_And_The_Archive_File_Never_Exceeds_The_Ceiling()
    {
        const long ceiling = 4096;
        var factory = Factory(ceiling);
        using var workspace = factory.CreateWorkspace(Guid.NewGuid());
        await using var archive = workspace.OpenArchiveStream();
        await using var budgeted = workspace.CreateBudgetEnforcingWriteStream(archive);

        var archivePath = archive.Name;

        await Assert.ThrowsAsync<OrganisationDataExportTempCapacityException>(() =>
            _builder.BuildToStreamAsync(
                One(HighEntropyStreamedTable("audit_log", rows: 50_000)),
                [],
                (_, _) => Task.FromResult<Stream?>(null),
                budgeted,
                workspace.EnsureWithinBudget,
                CancellationToken.None));

        await archive.FlushAsync();
        // The offending write is aborted before any bytes land, so the file is at or under the ceiling.
        Assert.True(new FileInfo(archivePath).Length <= ceiling,
            $"archive grew to {new FileInfo(archivePath).Length} bytes, ceiling was {ceiling}");
    }

    [Fact]
    public async Task A_Large_Document_Entry_Larger_Than_The_Ceiling_Trips_Mid_Copy()
    {
        const long ceiling = 4096;
        var factory = Factory(ceiling);
        using var workspace = factory.CreateWorkspace(Guid.NewGuid());
        await using var archive = workspace.OpenArchiveStream();
        await using var budgeted = workspace.CreateBudgetEnforcingWriteStream(archive);

        var payload = new byte[512 * 1024];
        System.Security.Cryptography.RandomNumberGenerator.Fill(payload);
        var entries = new List<DocumentExportFileEntry> { new("documents/x/big.bin", "k0") };

        await Assert.ThrowsAsync<OrganisationDataExportTempCapacityException>(() =>
            _builder.BuildToStreamAsync(
                One(new DataExportTable("t", ["A"], [])),
                entries,
                (_, _) => Task.FromResult<Stream?>(new MemoryStream(payload, writable: false)),
                budgeted,
                workspace.EnsureWithinBudget,
                CancellationToken.None));

        await archive.FlushAsync();
        Assert.True(new FileInfo(archive.Name).Length <= ceiling);
    }

    [Fact]
    public async Task Table_Data_Fits_But_ZIP_Finalisation_Pushes_Past_The_Ceiling_And_Still_Trips()
    {
        // Build once with a generous ceiling to measure the finished archive size, then re-run with a
        // ceiling set just below it: the entry bytes fit, but the central-directory / end-of-archive
        // write during ZipArchive.Dispose pushes past the ceiling and must throw.
        // Deterministic content so both builds below produce a byte-identical archive; the second run's
        // ceiling is then reliably a few bytes under what the finalisation write needs.
        var table = HighEntropyStreamedTable("audit_log", rows: 400, seed: 20260910);

        long finishedSize;
        {
            var factory = Factory(long.MaxValue);
            using var workspace = factory.CreateWorkspace(Guid.NewGuid());
            await using var archive = workspace.OpenArchiveStream();
            await _builder.BuildToStreamAsync(
                One(table), [], (_, _) => Task.FromResult<Stream?>(null), archive, null, CancellationToken.None);
            await archive.FlushAsync();
            finishedSize = archive.Length;
        }

        {
            // A ceiling a handful of bytes under the finished size: the last entry write and/or the
            // central directory write crosses it.
            var factory = Factory(finishedSize - 8);
            using var workspace = factory.CreateWorkspace(Guid.NewGuid());
            await using var archive = workspace.OpenArchiveStream();
            await using var budgeted = workspace.CreateBudgetEnforcingWriteStream(archive);

            await Assert.ThrowsAsync<OrganisationDataExportTempCapacityException>(() =>
                _builder.BuildToStreamAsync(
                    One(HighEntropyStreamedTable("audit_log", rows: 400, seed: 20260910)),
                    [],
                    (_, _) => Task.FromResult<Stream?>(null),
                    budgeted,
                    workspace.EnsureWithinBudget,
                    CancellationToken.None));
        }
    }

    [Fact]
    public async Task A_Streamed_Table_Well_Within_The_Ceiling_Builds_A_Valid_Zip_Through_The_Budgeted_Stream()
    {
        var factory = Factory(maxArchive: 1 << 20);
        using var workspace = factory.CreateWorkspace(Guid.NewGuid());
        await using var archive = workspace.OpenArchiveStream();
        await using var budgeted = workspace.CreateBudgetEnforcingWriteStream(archive);

        await _builder.BuildToStreamAsync(
            One(HighEntropyStreamedTable("audit_log", rows: 25)),
            [],
            (_, _) => Task.FromResult<Stream?>(null),
            budgeted,
            workspace.EnsureWithinBudget,
            CancellationToken.None);

        archive.Position = 0;
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        var entry = Assert.Single(zip.Entries);
        Assert.Equal("audit_log.csv", entry.FullName);
        using var reader = new StreamReader(entry.Open());
        var lines = (await reader.ReadToEndAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(26, lines.Length); // header + 25 rows
    }
}
