using HR.Modules.Reporting.Domain;

namespace HR.Modules.Reporting.Tests;

public class OrganisationDataExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static OrganisationDataExport NewPending() =>
        OrganisationDataExport.Create(Guid.NewGuid(), Guid.NewGuid(), "Admin", Now);

    [Fact]
    public void Create_Starts_Pending_With_No_Terminal_Timestamps()
    {
        var export = NewPending();

        Assert.Equal(OrganisationDataExport.StatusPending, export.Status);
        Assert.Equal(Now, export.RequestedAt);
        Assert.Null(export.StartedAt);
        Assert.Null(export.CompletedAt);
        Assert.Equal(0, export.DownloadCount);
    }

    [Fact]
    public void MarkInProgress_From_Pending_Succeeds()
    {
        var export = NewPending();
        var result = export.MarkInProgress(Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrganisationDataExport.StatusInProgress, export.Status);
        Assert.Equal(Now.AddMinutes(1), export.StartedAt);
    }

    [Fact]
    public void MarkInProgress_From_Non_Pending_Is_Conflict()
    {
        var export = NewPending();
        export.MarkInProgress(Now);

        var result = export.MarkInProgress(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public void MarkCompleted_Sets_Storage_Size_And_Seven_Day_Expiry()
    {
        var export = NewPending();
        export.MarkInProgress(Now);

        var result = export.MarkCompleted("organisation-exports/c/e.zip", 2048, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrganisationDataExport.StatusCompleted, export.Status);
        Assert.Equal("organisation-exports/c/e.zip", export.StorageKey);
        Assert.Equal(2048, export.FileSizeBytes);
        Assert.Equal(Now.AddMinutes(5).AddDays(7), export.ExpiresAt);
        Assert.True(export.IsDownloadable(Now.AddMinutes(5)));
    }

    [Fact]
    public void MarkCompleted_Requires_InProgress_And_Storage_Key()
    {
        var export = NewPending();
        Assert.True(export.MarkCompleted("k", 1, Now).IsFailure);

        export.MarkInProgress(Now);
        Assert.Equal("validation", export.MarkCompleted("  ", 1, Now).Error.Code);
    }

    [Fact]
    public void MarkFailed_From_Completed_Is_Conflict()
    {
        var export = NewPending();
        export.MarkInProgress(Now);
        export.MarkCompleted("k", 1, Now);

        var result = export.MarkFailed("boom", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public void MarkFailed_From_InProgress_Uses_Default_Reason_When_Blank()
    {
        var export = NewPending();
        export.MarkInProgress(Now);

        var result = export.MarkFailed("   ", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrganisationDataExport.StatusFailed, export.Status);
        Assert.Equal("Export could not be generated.", export.FailureReason);
    }

    [Fact]
    public void MarkExpired_Only_From_Completed_And_Clears_Storage_Key()
    {
        var export = NewPending();
        export.MarkInProgress(Now);
        export.MarkCompleted("k", 1, Now);

        var result = export.MarkExpired(Now.AddDays(8));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrganisationDataExport.StatusExpired, export.Status);
        Assert.Null(export.StorageKey);
    }

    [Fact]
    public void RecordDownload_Succeeds_While_Completed_And_Not_Expired()
    {
        var export = NewPending();
        export.MarkInProgress(Now);
        export.MarkCompleted("k", 1, Now);

        var user = Guid.NewGuid();
        var result = export.RecordDownload(user, Now.AddDays(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, export.DownloadCount);
        Assert.Equal(Now.AddDays(1), export.LastDownloadedAt);
        Assert.Equal(user, export.LastDownloadedByUserId);
    }

    [Fact]
    public void RecordDownload_After_Expiry_Is_Conflict()
    {
        var export = NewPending();
        export.MarkInProgress(Now);
        export.MarkCompleted("k", 1, Now);

        var result = export.RecordDownload(Guid.NewGuid(), Now.AddDays(30));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }
}
