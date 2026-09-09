using HR.Modules.Reporting.Domain;

namespace HR.Modules.Reporting.Tests;

/// <summary>
/// Ticket 3K: durable retry cursors for the organisation-data-export artefact-cleanup sweep.
/// Covers <see cref="OrganisationDataExport.DeferArtefactCleanup"/>,
/// <see cref="OrganisationDataExport.RecordLateUploadRecheck"/>, the capped-exponential backoff growth
/// and ceiling, the <see cref="OrganisationDataExport.MarkAttemptFilesCleaned"/> cursor reset, and the
/// state guards. Every test also pins that the export's <c>FailureReason</c> is never mutated by any of
/// these cleanup-bookkeeping methods.
/// </summary>
public class OrganisationDataExportCleanupCursorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Token = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static OrganisationDataExport FailedExport(string reason = "boom")
    {
        var export = OrganisationDataExport.Create(Guid.NewGuid(), Guid.NewGuid(), "Admin", Now.AddDays(-1));
        export.BeginAttempt(Token, Now.AddDays(-1));
        export.MarkFailed(reason, Now.AddDays(-1));
        return export;
    }

    private static OrganisationDataExport CompletedExport()
    {
        var export = OrganisationDataExport.Create(Guid.NewGuid(), Guid.NewGuid(), "Admin", Now.AddDays(-1));
        export.BeginAttempt(Token, Now.AddDays(-1));
        export.MarkCompleted(Token, "published.zip", 1, Now.AddDays(-1));
        return export;
    }

    // ----- DeferArtefactCleanup -----

    [Fact]
    public void DeferArtefactCleanup_From_Non_Terminal_Is_Conflict()
    {
        var pending = OrganisationDataExport.Create(Guid.NewGuid(), Guid.NewGuid(), "Admin", Now);

        var result = pending.DeferArtefactCleanup(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Null(pending.ArtefactCleanupNextAttemptAt);
        Assert.Equal(0, pending.ArtefactCleanupAttemptCount);
    }

    [Fact]
    public void DeferArtefactCleanup_When_Already_Cleaned_Is_Conflict()
    {
        var export = FailedExport();
        export.MarkAttemptFilesCleaned(Now);

        var result = export.DeferArtefactCleanup(Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Null(export.ArtefactCleanupNextAttemptAt);
        Assert.Equal("boom", export.FailureReason);
    }

    [Fact]
    public void DeferArtefactCleanup_First_Attempt_Schedules_Fifteen_Minutes_Out_And_Leaves_FailureReason()
    {
        var export = FailedExport();

        var result = export.DeferArtefactCleanup(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, export.ArtefactCleanupAttemptCount);
        Assert.Equal(Now.AddMinutes(15), export.ArtefactCleanupNextAttemptAt);
        Assert.Equal("boom", export.FailureReason);
        Assert.Null(export.AttemptFilesCleanedAt);
    }

    [Fact]
    public void DeferArtefactCleanup_Backoff_Doubles_Each_Attempt()
    {
        var export = FailedExport();

        export.DeferArtefactCleanup(Now);
        Assert.Equal(Now.AddMinutes(15), export.ArtefactCleanupNextAttemptAt);

        export.DeferArtefactCleanup(Now);
        Assert.Equal(2, export.ArtefactCleanupAttemptCount);
        Assert.Equal(Now.AddMinutes(30), export.ArtefactCleanupNextAttemptAt);

        export.DeferArtefactCleanup(Now);
        Assert.Equal(Now.AddMinutes(60), export.ArtefactCleanupNextAttemptAt);

        export.DeferArtefactCleanup(Now);
        Assert.Equal(Now.AddMinutes(120), export.ArtefactCleanupNextAttemptAt);
    }

    [Fact]
    public void DeferArtefactCleanup_Backoff_Is_Capped_At_Twenty_Four_Hours()
    {
        var export = FailedExport();

        for (var i = 0; i < 20; i++)
            export.DeferArtefactCleanup(Now);

        Assert.Equal(20, export.ArtefactCleanupAttemptCount);
        Assert.Equal(Now.AddHours(24), export.ArtefactCleanupNextAttemptAt);
        Assert.Equal("boom", export.FailureReason);
    }

    // ----- MarkAttemptFilesCleaned cursor reset -----

    [Fact]
    public void MarkAttemptFilesCleaned_Resets_The_Deferred_Cleanup_Cursor()
    {
        var export = FailedExport();
        export.DeferArtefactCleanup(Now);
        export.DeferArtefactCleanup(Now);
        Assert.NotNull(export.ArtefactCleanupNextAttemptAt);

        var result = export.MarkAttemptFilesCleaned(Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddMinutes(5), export.AttemptFilesCleanedAt);
        Assert.Null(export.ArtefactCleanupNextAttemptAt);
        Assert.Equal(0, export.ArtefactCleanupAttemptCount);
        Assert.Equal("boom", export.FailureReason);
    }

    // ----- RecordLateUploadRecheck -----

    [Fact]
    public void RecordLateUploadRecheck_With_No_Completed_Cleanup_Is_Conflict()
    {
        var export = FailedExport();

        var result = export.RecordLateUploadRecheck(succeeded: true, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Null(export.LateUploadRecheckNextAt);
        Assert.Equal("boom", export.FailureReason);
    }

    [Fact]
    public void RecordLateUploadRecheck_Success_Inside_Window_Schedules_Next_Day_And_Resets_Count()
    {
        var export = FailedExport();
        export.MarkAttemptFilesCleaned(Now.AddDays(-5));
        export.RecordLateUploadRecheck(succeeded: false, Now); // bump the failure count first

        var result = export.RecordLateUploadRecheck(succeeded: true, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, export.LateUploadRecheckAttemptCount);
        Assert.Equal(Now.AddDays(1), export.LateUploadRecheckNextAt);
        Assert.Equal("boom", export.FailureReason);
    }

    [Fact]
    public void RecordLateUploadRecheck_Success_On_The_Fourteen_Day_Boundary_Is_Still_In_Window()
    {
        var export = FailedExport();
        export.MarkAttemptFilesCleaned(Now.AddDays(-OrganisationDataExport.LateUploadRecheckWindowDays));

        var result = export.RecordLateUploadRecheck(succeeded: true, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddDays(1), export.LateUploadRecheckNextAt);
    }

    [Fact]
    public void RecordLateUploadRecheck_Success_Just_Past_The_Window_Stops_Rechecking()
    {
        var export = FailedExport();
        export.MarkAttemptFilesCleaned(Now.AddDays(-OrganisationDataExport.LateUploadRecheckWindowDays).AddTicks(-1));

        var result = export.RecordLateUploadRecheck(succeeded: true, Now);

        Assert.True(result.IsSuccess);
        Assert.Null(export.LateUploadRecheckNextAt);
        Assert.Equal(0, export.LateUploadRecheckAttemptCount);
    }

    [Fact]
    public void RecordLateUploadRecheck_Success_Well_Outside_The_Window_Stops_Rechecking()
    {
        var export = FailedExport();
        export.MarkAttemptFilesCleaned(Now.AddDays(-20));

        export.RecordLateUploadRecheck(succeeded: true, Now);

        Assert.Null(export.LateUploadRecheckNextAt);
    }

    [Fact]
    public void RecordLateUploadRecheck_Failure_Increments_Count_And_Backs_Off_Keeping_Cursor_Set()
    {
        var export = FailedExport();
        export.MarkAttemptFilesCleaned(Now.AddDays(-20)); // already past the window

        var first = export.RecordLateUploadRecheck(succeeded: false, Now);
        Assert.True(first.IsSuccess);
        Assert.Equal(1, export.LateUploadRecheckAttemptCount);
        Assert.Equal(Now.AddMinutes(15), export.LateUploadRecheckNextAt);

        export.RecordLateUploadRecheck(succeeded: false, Now);
        Assert.Equal(2, export.LateUploadRecheckAttemptCount);
        Assert.Equal(Now.AddMinutes(30), export.LateUploadRecheckNextAt);

        Assert.Equal("boom", export.FailureReason);
    }

    [Fact]
    public void RecordLateUploadRecheck_Failure_Backoff_Is_Capped_At_Twenty_Four_Hours()
    {
        var export = FailedExport();
        export.MarkAttemptFilesCleaned(Now.AddDays(-1));

        for (var i = 0; i < 20; i++)
            export.RecordLateUploadRecheck(succeeded: false, Now);

        Assert.Equal(Now.AddHours(24), export.LateUploadRecheckNextAt);
    }

    [Fact]
    public void Cleanup_Bookkeeping_Never_Mutates_FailureReason_On_A_Completed_Export()
    {
        var export = CompletedExport();
        Assert.Null(export.FailureReason);

        export.DeferArtefactCleanup(Now);
        export.DeferArtefactCleanup(Now);
        export.MarkAttemptFilesCleaned(Now);
        export.RecordLateUploadRecheck(succeeded: false, Now);
        export.RecordLateUploadRecheck(succeeded: true, Now);

        Assert.Null(export.FailureReason);
    }
}
