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

        workspace.Dispose();
    }

    [Fact]
    public void EnsureWithinBudget_Throws_Only_When_The_Archive_Exceeds_The_Per_Export_Ceiling()
    {
        var factory = Factory(Tiny(maxArchive: 1000));
        using var workspace = factory.CreateWorkspace(Guid.NewGuid());

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

        Assert.Empty(Directory.GetDirectories(_root));
    }

    [Fact]
    public void CreateWorkspace_Succeeds_When_The_Work_Root_Is_Just_Under_The_Total_Budget()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "existing-archive.bin"), new byte[1000]);

        var factory = Factory(Tiny(maxArchive: 512, maxTotal: 4096));

        using var workspace = factory.CreateWorkspace(Guid.NewGuid());
        Assert.NotNull(workspace);
    }

    // ----- Ticket 4 (third hardening pass): combined-budget reservation -----

    [Fact]
    public void A_second_concurrent_CreateWorkspace_Is_Refused_When_Two_Reservation_Units_Exceed_The_Total()
    {
        var factory = Factory(Tiny(maxArchive: 1000, maxTotal: 1500));

        using var first = factory.CreateWorkspace(Guid.NewGuid());

        var ex = Assert.Throws<OrganisationDataExportTempCapacityException>(
            () => factory.CreateWorkspace(Guid.NewGuid()));
        Assert.Contains("cannot be reserved", ex.Message);

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

            using (var archive = workspace.OpenArchiveStream())
            {
                archive.Write("x"u8);
            }

            var dir = Directory.GetDirectories(_root).Single();
            Directory.Delete(dir, recursive: true);

            workspace.Dispose();
            workspace.Dispose();
        }

        using var final = factory.CreateWorkspace(Guid.NewGuid());
        Assert.NotNull(final);
    }

    [Fact]
    public void Three_Reservations_Fit_And_The_Fourth_Is_Refused_When_Sized_For_Exactly_Three()
    {
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
        var factory = Factory(Tiny(maxArchive: 1200, maxTotal: 2600));

        var first = factory.CreateWorkspace(Guid.NewGuid());
        using (var archive = first.OpenArchiveStream())
        {
            archive.Write(new byte[300]);
            archive.Flush();
        }

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

        var second = factory.CreateWorkspace(Guid.NewGuid());
        Assert.NotNull(second);

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void Concurrent_Admission_Never_Exceeds_The_Budget()
    {
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
        var factory = Factory(Tiny(maxArchive: 1000, maxTotal: 1000));

        for (var i = 0; i < 6; i++)
        {
            var ws = factory.CreateWorkspace(Guid.NewGuid());

            Assert.Throws<OrganisationDataExportTempCapacityException>(
                () => factory.CreateWorkspace(Guid.NewGuid()));

            ws.Dispose();
        }

        using var final = factory.CreateWorkspace(Guid.NewGuid());
        Assert.NotNull(final);
    }

    [Fact]
    public void Repeated_Disposal_Frees_Exactly_One_Slot_Not_Two()
    {
        var factory = Factory(Tiny(maxArchive: 1000, maxTotal: 2000));

        var a = factory.CreateWorkspace(Guid.NewGuid());
        var b = factory.CreateWorkspace(Guid.NewGuid());

        a.Dispose();
        a.Dispose(); // a second disposal must NOT release a second reservation

        var c = factory.CreateWorkspace(Guid.NewGuid());
        Assert.NotNull(c);

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
        var factory = Factory(Tiny(maxArchive: 1000, maxTotal: 1500));

        var ws = factory.CreateWorkspace(Guid.NewGuid());

        var held = ws.OpenArchiveStream();
        held.Write(new byte[800]);
        held.Flush();

        ws.Dispose();

        if (Directory.GetDirectories(_root).Length == 0)
        {
            var leftover = Directory.CreateDirectory(Path.Combine(_root, Guid.NewGuid().ToString("N")));
            File.WriteAllBytes(Path.Combine(leftover.FullName, "archive.zip"), new byte[800]);
        }

        try
        {
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
