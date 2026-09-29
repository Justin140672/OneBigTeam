namespace HR.Infrastructure.Abstractions;

/// <summary>Ticket 4: raised when temporary working storage for an export is exhausted or too low.</summary>
public sealed class OrganisationDataExportTempCapacityException(string message) : Exception(message);

/// <summary>
/// Ticket 4: a bounded, self-cleaning scratch directory for assembling one export archive on temp
/// disk. Created via <see cref="IOrganisationDataExportWorkspaceFactory"/>. Disposal deletes the
/// directory (and releases the workspace's slice of the process-wide temp-disk reservation); a hard
/// process kill leaves it for the orphan sweep.
/// </summary>
public interface IOrganisationDataExportWorkspace : IDisposable
{
    FileStream OpenArchiveStream();

    long MaxArchiveBytes { get; }

    Stream CreateBudgetEnforcingWriteStream(Stream inner);

    void EnsureWithinBudget(long currentArchiveBytes);
}

public interface IOrganisationDataExportWorkspaceFactory
{
    IOrganisationDataExportWorkspace CreateWorkspace(Guid exportId);

    int SweepOrphans(DateTimeOffset now);
}

public sealed class OrganisationDataExportWorkspaceFactory : IOrganisationDataExportWorkspaceFactory
{
    private readonly string _root;
    private readonly OrganisationDataExportResourceLimits _limits;

    // Ticket 4: process-scoped (per work-root) reservation accounting. Registered as a singleton in
    // production, so this instance state is effectively process-wide; kept per-instance so tests with
    // isolated work roots stay independent. Guards against concurrent builds jointly overshooting the
    // combined budget.
    //
    // Ticket 4 final follow-up: admission is now
    //     orphan/unreserved bytes on disk  +  sum of active workspace reservations  +  this reservation
    // and must stay within MaxTotalWorkspaceBytes. Bytes written *inside* an active workspace directory
    // are already covered by that workspace's reservation, so they are excluded from the orphan figure
    // rather than counted a second time. When a workspace is disposed its reservation is released and
    // its directory stops being "active" — so any files a failed delete left behind immediately revert
    // to counting as orphan bytes against the next admission.
    private readonly object _reservationLock = new();

    private readonly Dictionary<string, long> _activeReservations = new(StringComparer.OrdinalIgnoreCase);

    public OrganisationDataExportWorkspaceFactory(
        OrganisationDataExportResourceLimits? limits = null, string? rootOverride = null)
    {
        _limits = limits ?? OrganisationDataExportResourceLimits.Default;
        _root = rootOverride ?? Path.Combine(Path.GetTempPath(), "onebigteam", "organisation-export-work");
    }

    private long ReservationUnit => Math.Min(_limits.MaxArchiveBytesPerExport, _limits.MaxTotalWorkspaceBytes);

    public IOrganisationDataExportWorkspace CreateWorkspace(Guid exportId)
    {
        Directory.CreateDirectory(_root);

        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_root))!);
            if (drive.IsReady && drive.AvailableFreeSpace < _limits.MinimumFreeDiskBytes)
            {
                throw new OrganisationDataExportTempCapacityException(
                    $"Not enough free disk for an organisation data export ({drive.AvailableFreeSpace:N0} bytes free, {_limits.MinimumFreeDiskBytes:N0} required).");
            }
        }
        catch (Exception ex) when (ex is not OrganisationDataExportTempCapacityException)
        {
            // A failed free-space probe must not block exports; the FileStream write is the backstop.
        }

        var unit = ReservationUnit;
        string dir;
        lock (_reservationLock)
        {
            var orphanBytes = OrphanBytesLocked();

            if (orphanBytes >= _limits.MaxTotalWorkspaceBytes)
            {
                throw new OrganisationDataExportTempCapacityException(
                    $"Organisation data export working storage is full ({orphanBytes:N0} of {_limits.MaxTotalWorkspaceBytes:N0} bytes used).");
            }

            var activeReservations = 0L;
            foreach (var reserved in _activeReservations.Values)
                activeReservations += reserved;

            if (orphanBytes + activeReservations + unit > _limits.MaxTotalWorkspaceBytes)
            {
                throw new OrganisationDataExportTempCapacityException(
                    $"Organisation data export working storage cannot be reserved for another build " +
                    $"({orphanBytes:N0} orphan bytes on disk, {activeReservations:N0} bytes reserved by {_activeReservations.Count} active build(s), {unit:N0} bytes requested, {_limits.MaxTotalWorkspaceBytes:N0} bytes total).");
            }

            dir = Path.Combine(_root, $"{exportId:N}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            _activeReservations[dir] = unit;
        }

        try
        {
            return new Workspace(dir, unit, () => ReleaseReservation(dir));
        }
        catch
        {
            ReleaseReservation(dir);
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { /* orphan sweep retries */ }
            throw;
        }
    }

    private long OrphanBytesLocked()
    {
        if (!Directory.Exists(_root))
            return 0;

        var total = 0L;
        foreach (var entry in Directory.EnumerateFileSystemEntries(_root))
        {
            if (Directory.Exists(entry))
            {
                if (_activeReservations.ContainsKey(entry))
                    continue;
                total += DirectorySize(entry);
            }
            else
            {
                try { total += new FileInfo(entry).Length; }
                catch { /* transient — ignore */ }
            }
        }

        return total;
    }

    private void ReleaseReservation(string dir)
    {
        lock (_reservationLock)
        {
            _activeReservations.Remove(dir);
        }
    }

    public int SweepOrphans(DateTimeOffset now)
    {
        if (!Directory.Exists(_root))
            return 0;

        var cutoff = now - _limits.OrphanWorkspaceAge;
        var removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(dir) >= cutoff.UtcDateTime)
                    continue;

                Directory.Delete(dir, recursive: true);
                removed++;
            }
            catch
            {
            }
        }

        return removed;
    }

    private static long DirectorySize(string path)
    {
        var total = 0L;
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { total += new FileInfo(file).Length; }
            catch { /* transient — ignore */ }
        }

        return total;
    }

    private sealed class Workspace(string directory, long maxArchiveBytes, Action releaseReservation)
        : IOrganisationDataExportWorkspace
    {
        private readonly string _archivePath = Path.Combine(directory, "export.zip");
        private int _disposed;

        public long MaxArchiveBytes => maxArchiveBytes;

        public FileStream OpenArchiveStream() =>
            new(_archivePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, bufferSize: 1 << 20);

        public Stream CreateBudgetEnforcingWriteStream(Stream inner) =>
            new OrganisationDataExportArchiveBudgetStream(inner, maxArchiveBytes);

        public void EnsureWithinBudget(long currentArchiveBytes)
        {
            if (currentArchiveBytes > maxArchiveBytes)
            {
                throw new OrganisationDataExportTempCapacityException(
                    $"The export archive exceeded the per-export temp-disk ceiling ({maxArchiveBytes:N0} bytes).");
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
            finally
            {
                releaseReservation();
            }
        }
    }
}

/// <summary>
/// Ticket 4: write-through counting stream over the export archive <see cref="FileStream"/>. Before
/// any write completes it checks whether the write would push the archive's high-water length past the
/// per-export ceiling and, if so, throws <see cref="OrganisationDataExportTempCapacityException"/>
/// without writing a single byte — so an oversized CSV row, an oversized document copy, or the ZIP
/// central-directory / finalisation write all trip mid-write rather than only at entry boundaries.
/// Never disposes the inner stream (the build job owns it for the subsequent upload).
/// </summary>
public sealed class OrganisationDataExportArchiveBudgetStream(Stream inner, long maxBytes) : Stream
{
    private long _highWater;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    private void GuardWrite(long count)
    {
        var end = inner.Position + count;
        if (end > _highWater)
            _highWater = end;

        if (_highWater > maxBytes)
        {
            throw new OrganisationDataExportTempCapacityException(
                $"The export archive exceeded the per-export temp-disk ceiling ({maxBytes:N0} bytes) while writing.");
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        GuardWrite(count);
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        GuardWrite(buffer.Length);
        inner.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        GuardWrite(count);
        return inner.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        GuardWrite(buffer.Length);
        return inner.WriteAsync(buffer, cancellationToken);
    }

    public override void WriteByte(byte value)
    {
        GuardWrite(1);
        inner.WriteByte(value);
    }

    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        // Intentionally does not dispose the inner stream.
    }

    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
