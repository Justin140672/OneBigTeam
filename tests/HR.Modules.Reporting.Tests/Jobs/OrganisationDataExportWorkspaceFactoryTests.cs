using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Jobs;

/// <summary>
/// Ticket 4: unit coverage for the bounded, self-cleaning temp workspace used to assemble one export
/// archive on disk. Every test gets its own unique work root under the OS temp path so the suite stays
/// deterministic at high parallelism.
/// </summary>
public sealed class OrganisationDataExportWorkspaceFactoryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "obt-test-workspace", Guid.NewGuid().ToString("N"));

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

    private OrganisationDataExportWorkspaceFactory Factory(OrganisationDataExportResourceLimits limits) =>
        new(limits, rootOverride: _root);

    private static OrganisationDataExportResourceLimits Tiny(
        long maxArchive = 4096,
        long maxTotal = long.MaxValue,
        long minFreeDisk = 0,
        TimeSpan? orphanAge = null) =>
        new()
        {
            MaxArchiveBytesPerExport = maxArchive,
            MaxTotalWorkspaceBytes = maxTotal,
            MinimumFreeDiskBytes = minFreeDisk,
            OrphanWorkspaceAge = orphanAge ?? TimeSpan.FromHours(6),
        };

    [Fact]
    public void CreateWorkspace_Returns_A_Workspace_With_A_Writable_Archive_Stream_And_Dispose_Deletes_The_Directory()
    {
        var factory = Factory(Tiny());
        var exportId = Guid.NewGuid();

        var workspace = factory.CreateWorkspace(exportId);

        Assert.Single(Directory.GetDirectories(_root));

        string archiveDir;
        using (var stream = workspace.OpenArchiveStream())
        {
            Assert.True(stream.CanWrite);
            Assert.True(stream.CanRead);
            stream.Write("hello world"u8);
            stream.Flush();
            archiveDir = Path.GetDirectoryName(stream.Name)!;
            Assert.True(File.Exists(stream.Name));
        }

        workspace.Dispose();

        Assert.False(Directory.Exists(archiveDir));
        Assert.Empty(Directory.GetDirectories(_root));

        // Dispose is idempotent.
        workspace.Dispose();
    }

    [Fact]
    public void EnsureWithinBudget_Throws_Only_When_The_Archive_Exceeds_The_Per_Export_Ceiling()
    {
        var factory = Factory(Tiny(maxArchive: 1000));
        using var workspace = factory.CreateWorkspace(Guid.NewGuid());

        // Boundary: exactly at the ceiling is allowed; strictly greater is refused.
        workspace.EnsureWithinBudget(0);
        workspace.EnsureWithinBudget(999);
        workspace.EnsureWithinBudget(1000);

        var ex = Assert.Throws<OrganisationDataExportTempCapacityException>(
            () => workspace.EnsureWithinBudget(1001));
        Assert.Contains("per-export", ex.Message);
    }

    [Fact]
    public void CreateWorkspace_Is_Refused_When_The_Work_Root_Already_Holds_The_Total_Budget()
    {
        Directory.CreateDirectory(_root);
        var bigFile = Path.Combine(_root, "existing-archive.bin");
        File.WriteAllBytes(bigFile, new byte[8192]);

        var factory = Factory(Tiny(maxTotal: 4096));

        var ex = Assert.Throws<OrganisationDataExportTempCapacityException>(
            () => factory.CreateWorkspace(Guid.NewGuid()));
        Assert.Contains("working storage is full", ex.Message);

        // Nothing new was created.
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public void CreateWorkspace_Succeeds_When_The_Work_Root_Is_Just_Under_The_Total_Budget()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "existing-archive.bin"), new byte[1000]);

        // Reservation unit = Min(maxArchive, maxTotal) = 512, so used (1000) + unit (512) = 1512 <= 4096.
        var factory = Factory(Tiny(maxArchive: 512, maxTotal: 4096));

        using var workspace = factory.CreateWorkspace(Guid.NewGuid());
        Assert.NotNull(workspace);
    }

    // ----- Ticket 4 (third hardening pass): combined-budget reservation -----

    [Fact]
    public void A_second_concurrent_CreateWorkspace_Is_Refused_When_Two_Reservation_Units_Exceed_The_Total()
    {
        // unit = 1000; maxTotal = 1500 -> one build fits, a second concurrent one does not.
        var factory = Factory(Tiny(maxArchive: 1000, maxTotal: 1500));

        using var first = factory.CreateWorkspace(Guid.NewGuid());

        var ex = Assert.Throws<OrganisationDataExportTempCapacityException>(
            () => factory.CreateWorkspace(Guid.NewGuid()));
        Assert.Contains("cannot be reserved", ex.Message);

        // Only the first build's directory exists; the refused call created nothing.
        Assert.Single(Directory.GetDirectories(_root));
    }

    [Fact]
    public void Disposing_A_Workspace_Releases_Its_Reservation_So_The_Next_CreateWorkspace_Succeeds()
    {
        var factory = Factory(Tiny(maxArchive: 1000, maxTotal: 1500));

        var first = factory.CreateWorkspace(Guid.NewGuid());
        Assert.Throws<OrganisationDataExportTempCapacityException>(
            () => factory.CreateWorkspace(Guid.NewGuid()));

        first.Dispose();

        using var second = factory.CreateWorkspace(Guid.NewGuid());
        Assert.NotNull(second);
    }

    [Fact]
    public void Pre_Existing_Orphan_Bytes_Count_Against_The_Reservation_Even_With_No_Outstanding_Reservations()
    {
        Directory.CreateDirectory(_root);
        // 1200 orphan bytes on disk + a 1000-byte reservation unit > 1500 total, with _reservedBytes == 0.
        File.WriteAllBytes(Path.Combine(_root, "orphan.bin"), new byte[1200]);

        var factory = Factory(Tiny(maxArchive: 1000, maxTotal: 1500));

        var ex = Assert.Throws<OrganisationDataExportTempCapacityException>(
            () => factory.CreateWorkspace(Guid.NewGuid()));
        Assert.Contains("cannot be reserved", ex.Message);
        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public void Reservation_Is_Balanced_Even_When_Dispose_Has_Nothing_To_Delete_And_Is_Repeated()
    {
        var factory = Factory(Tiny(maxArchive: 1000, maxTotal: 1500));

        for (var i = 0; i < 5; i++)
        {
            var workspace = factory.CreateWorkspace(Guid.NewGuid());

            // Delete the directory out from under the workspace so Dispose's delete is a no-op,
            // then dispose twice — the reservation must still be released exactly once.
            using (var archive = workspace.OpenArchiveStream())
            {
                archive.Write("x"u8);
            }

            var dir = Directory.GetDirectories(_root).Single();
            Directory.Delete(dir, recursive: true);

            workspace.Dispose();
            workspace.Dispose();
        }

        // If any of the 5 iterations leaked its reservation, this final acquire would be refused.
        using var final = factory.CreateWorkspace(Guid.NewGuid());
        Assert.NotNull(final);
    }

    [Fact]
    public void Three_Reservations_Fit_And_The_Fourth_Is_Refused_When_Sized_For_Exactly_Three()
    {
        // unit = 1000, total = 3500 -> exactly three concurrent builds fit (3000 <= 3500), a fourth does not.
        var factory = Factory(Tiny(maxArchive: 1000, maxTotal: 3500));

        var a = factory.CreateWorkspace(Guid.NewGuid());
        var b = factory.CreateWorkspace(Guid.NewGuid());
        var c = factory.CreateWorkspace(Guid.NewGuid());

        Assert.Throws<OrganisationDataExportTempCapacityException>(
            () => factory.CreateWorkspace(Guid.NewGuid()));

        a.Dispose();
        b.Dispose();
        c.Dispose();
    }

    [Fact]
    public void SweepOrphans_Removes_Only_Directories_Older_Than_The_Orphan_Age_And_Is_Idempotent()
    {
        var factory = Factory(Tiny(orphanAge: TimeSpan.FromHours(6)));
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        Directory.CreateDirectory(_root);
        var staleDir = Path.Combine(_root, "stale-" + Guid.NewGuid().ToString("N"));
        var freshDir = Path.Combine(_root, "fresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staleDir);
        Directory.CreateDirectory(freshDir);
        File.WriteAllText(Path.Combine(staleDir, "export.zip"), "x");

        Directory.SetLastWriteTimeUtc(staleDir, now.UtcDateTime.AddHours(-7));
        Directory.SetLastWriteTimeUtc(freshDir, now.UtcDateTime.AddHours(-1));

        var removed = factory.SweepOrphans(now);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(staleDir));
        Assert.True(Directory.Exists(freshDir));

        // Idempotent: a second sweep removes nothing more.
        Assert.Equal(0, factory.SweepOrphans(now));
    }

    [Fact]
    public void SweepOrphans_Keeps_A_Directory_Exactly_At_The_Boundary_Age()
    {
        var factory = Factory(Tiny(orphanAge: TimeSpan.FromHours(6)));
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        Directory.CreateDirectory(_root);
        var boundaryDir = Path.Combine(_root, "boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(boundaryDir);
        // cutoff == now - 6h; the implementation keeps dirs with LastWriteTimeUtc >= cutoff.
        Directory.SetLastWriteTimeUtc(boundaryDir, now.UtcDateTime.AddHours(-6));

        Assert.Equal(0, factory.SweepOrphans(now));
        Assert.True(Directory.Exists(boundaryDir));
    }

    [Fact]
    public void SweepOrphans_Is_Safe_When_The_Work_Root_Does_Not_Exist()
    {
        var factory = Factory(Tiny());
        Assert.False(Directory.Exists(_root));

        Assert.Equal(0, factory.SweepOrphans(DateTimeOffset.UtcNow));
    }

    // =====================================================================================
    //  Ticket 4 final follow-up: corrected capacity accounting
    //
    //  Admission is now:
    //      orphan bytes on disk (NOT inside an active workspace dir)
    //    + Σ active-workspace reservation units
    //    + this reservation unit
    //    <= MaxTotalWorkspaceBytes
    //
    //  Bytes written *inside* an active workspace are already covered by that workspace's own
    //  reservation, so they are excluded from the orphan figure rather than counted a second
    //  time (the double-count regression). A reservation is released exactly once — on disposal
    //  or creation failure — and files a failed cleanup leaves behind revert to counting as
    //  orphan bytes against the next admission.
    // =====================================================================================

    [Fact]
    public void Bytes_Inside_An_Active_Workspace_Are_Not_Double_Counted_Against_The_Next_Reservation()
    {
        // total 2600, unit = Min(1200, 2600) = 1200.
        var factory = Factory(Tiny(maxArchive: 1200, maxTotal: 2600));

        var first = factory.CreateWorkspace(Guid.NewGuid());
        using (var archive = first.OpenArchiveStream())
        {
            archive.Write(new byte[300]);
            archive.Flush();
        }

        // Old (buggy) accounting: 300 bytes on disk + 1200 (first reservation) + 1200 = 2700 > 2600 -> threw.
        // Corrected accounting: those 300 bytes live inside the still-active workspace dir, so orphan = 0;
        //                       0 + 1200 + 1200 = 2400 <= 2600 -> the second build is admitted.
        var second = factory.CreateWorkspace(Guid.NewGuid());
        Assert.NotNull(second);

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void A_Third_Reservation_Is_Refused_Once_Two_Active_Reservations_Fill_The_Budget()
    {
        var factory = Factory(Tiny(maxArchive: 1200, maxTotal: 2600));

        var first = factory.CreateWorkspace(Guid.NewGuid());
        using (var archive = first.OpenArchiveStream())
        {
            archive.Write(new byte[300]);
            archive.Flush();
        }

        var second = factory.CreateWorkspace(Guid.NewGuid());

        // 0 orphan + 2400 active reservations + 1200 requested = 3600 > 2600.
        var ex = Assert.Throws<OrganisationDataExportTempCapacityException>(
            () => factory.CreateWorkspace(Guid.NewGuid()));
        Assert.Contains("cannot be reserved", ex.Message);

        Assert.Equal(2, Directory.GetDirectories(_root).Length);

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void A_Loose_Orphan_File_Under_The_Root_Reduces_Available_Capacity_And_Can_Refuse_The_Next_Reservation()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "loose-orphan.bin"), new byte[300]);

        var factory = Factory(Tiny(maxArchive: 1200, maxTotal: 2600));

        var first = factory.CreateWorkspace(Guid.NewGuid());

        // 300 orphan + 1200 active + 1200 requested = 2700 > 2600.
        var ex = Assert.Throws<OrganisationDataExportTempCapacityException>(
            () => factory.CreateWorkspace(Guid.NewGuid()));
        Assert.Contains("cannot be reserved", ex.Message);

        first.Dispose();
    }

    [Fact]
    public void A_Small_Loose_Orphan_File_Still_Leaves_Room_For_A_Second_Reservation()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "loose-orphan.bin"), new byte[100]);

        var factory = Factory(Tiny(maxArchive: 1200, maxTotal: 2600));

        var first = factory.CreateWorkspace(Guid.NewGuid());

        // 100 orphan + 1200 active + 1200 requested = 2500 <= 2600 -> admitted.
        var second = factory.CreateWorkspace(Guid.NewGuid());
        Assert.NotNull(second);

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void Concurrent_Admission_Never_Exceeds_The_Budget()
    {
        // total 3500, unit 1000 -> exactly three of N concurrent callers may be admitted.
        var factory = Factory(Tiny(maxArchive: 1000, maxTotal: 3500));
        const int callers = 8;
        const int expectedWinners = 3;

        using var gate = new ManualResetEventSlim(false);
        var tasks = Enumerable.Range(0, callers).Select(_ => Task.Run(() =>
        {
            gate.Wait();
            try { return (IOrganisationDataExportWorkspace?)factory.CreateWorkspace(Guid.NewGuid()); }
            catch (OrganisationDataExportTempCapacityException) { return null; }
        })).ToArray();

        gate.Set();
        Task.WaitAll(tasks);

        var admitted = tasks.Select(t => t.Result).Where(w => w is not null).Select(w => w!).ToList();
        try
        {
            Assert.Equal(expectedWinners, admitted.Count);
            Assert.True(admitted.Sum(w => w.MaxArchiveBytes) <= 3500,
                "the sum of admitted reservations exceeded the total budget");
            Assert.Equal(admitted.Count, Directory.GetDirectories(_root).Length);
        }
        finally
        {
            foreach (var w in admitted)
                w.Dispose();
        }
    }

    [Fact]
    public void Success_Then_Dispose_Then_Reacquire_Cycles_Never_Leak_The_Single_Slot()
    {
        // room for exactly one build (unit = total = 1000).
        var factory = Factory(Tiny(maxArchive: 1000, maxTotal: 1000));

        for (var i = 0; i < 6; i++)
        {
            var ws = factory.CreateWorkspace(Guid.NewGuid());

            // While this one is live the budget is full.
            Assert.Throws<OrganisationDataExportTempCapacityException>(
                () => factory.CreateWorkspace(Guid.NewGuid()));

            // For the factory, "failure / cancellation" == the caller disposing without completing.
            ws.Dispose();
        }

        using var final = factory.CreateWorkspace(Guid.NewGuid());
        Assert.NotNull(final);
    }

    [Fact]
    public void Repeated_Disposal_Frees_Exactly_One_Slot_Not_Two()
    {
        // unit 1000, total 2000 -> exactly two concurrent builds.
        var factory = Factory(Tiny(maxArchive: 1000, maxTotal: 2000));

        var a = factory.CreateWorkspace(Guid.NewGuid());
        var b = factory.CreateWorkspace(Guid.NewGuid());

        a.Dispose();
        a.Dispose(); // a second disposal must NOT release a second reservation

        // Exactly one slot was freed: one build fits...
        var c = factory.CreateWorkspace(Guid.NewGuid());
        Assert.NotNull(c);

        // ...but a second does not — proving the double Dispose did not also free b's slot.
        Assert.Throws<OrganisationDataExportTempCapacityException>(
            () => factory.CreateWorkspace(Guid.NewGuid()));

        b.Dispose();
        using var d = factory.CreateWorkspace(Guid.NewGuid());
        Assert.NotNull(d);

        c.Dispose();
    }

    [Fact]
    public void Files_A_Failed_Cleanup_Left_Behind_Revert_To_Counting_As_Orphan_Bytes()
    {
        // unit 1000, total 1500 -> one build plus at most ~500 orphan bytes.
        var factory = Factory(Tiny(maxArchive: 1000, maxTotal: 1500));

        var ws = factory.CreateWorkspace(Guid.NewGuid());

        // Hold the archive handle open (FileShare.None) so Dispose's Directory.Delete throws and is
        // swallowed: the reservation is released but the 800 bytes stay on disk.
        var held = ws.OpenArchiveStream();
        held.Write(new byte[800]);
        held.Flush();

        ws.Dispose();

        // On Windows the open handle makes Directory.Delete fail and the leftover survives. On Linux an
        // open handle does not block deletion, so the scenario under test — "a failed cleanup left
        // files behind" — has to be recreated explicitly to stay OS-independent.
        if (Directory.GetDirectories(_root).Length == 0)
        {
            var leftover = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N")));
            File.WriteAllBytes(Path.Combine(leftover.FullName, "archive.zip"), new byte[800]);
        }

        try
        {
            // Dir is no longer active -> its 800 bytes now count as orphan.
            // 800 orphan + 0 active + 1000 requested = 1800 > 1500.
            var ex = Assert.Throws<OrganisationDataExportTempCapacityException>(
                () => factory.CreateWorkspace(Guid.NewGuid()));
            Assert.Contains("cannot be reserved", ex.Message);
        }
        finally
        {
            held.Dispose();
            foreach (var leftover in Directory.GetDirectories(_root))
            {
                try { Directory.Delete(leftover, recursive: true); } catch { /* best-effort */ }
            }
        }
    }
}
