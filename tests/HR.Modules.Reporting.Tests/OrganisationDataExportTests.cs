using HR.Modules.Reporting.Domain;

namespace HR.Modules.Reporting.Tests;

public class OrganisationDataExportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Token = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherToken = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static OrganisationDataExport NewPending() =>
        OrganisationDataExport.Create(Guid.NewGuid(), Guid.NewGuid(), "Admin", Now);

    private static OrganisationDataExport Claimed(DateTimeOffset? at = null)
    {
        var export = NewPending();
        export.BeginAttempt(Token, at ?? Now);
        return export;
    }

    [Fact]
    public void Create_Starts_Pending_With_No_Terminal_Timestamps()
    {
        var export = NewPending();

        Assert.Equal(OrganisationDataExport.StatusPending, export.Status);
        Assert.Equal(Now, export.RequestedAt);
        Assert.Null(export.StartedAt);
        Assert.Null(export.CompletedAt);
        Assert.Equal(0, export.DownloadCount);
        Assert.Null(export.LeaseOwnerToken);
        Assert.Null(export.LeaseAcquiredAt);
        Assert.Null(export.LeaseExpiresAt);
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
        var export = Claimed();

        var result = export.MarkCompleted(Token, "organisation-exports/c/e.zip", 2048, Now.AddMinutes(5));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrganisationDataExport.StatusCompleted, export.Status);
        Assert.Equal("organisation-exports/c/e.zip", export.StorageKey);
        Assert.Equal(2048, export.FileSizeBytes);
        Assert.Equal(Now.AddMinutes(5).AddDays(7), export.ExpiresAt);
        Assert.True(export.IsDownloadable(Now.AddMinutes(5)));
        Assert.Null(export.LeaseOwnerToken);
        Assert.Null(export.LeaseExpiresAt);
    }

    [Fact]
    public void MarkCompleted_Requires_InProgress_And_Storage_Key()
    {
        var pending = NewPending();
        Assert.True(pending.MarkCompleted(Token, "k", 1, Now).IsFailure);

        var export = Claimed();
        Assert.Equal("validation", export.MarkCompleted(Token, "  ", 1, Now).Error.Code);
    }

    [Fact]
    public void MarkCompleted_With_Non_Owner_Token_Is_Conflict_And_Leaves_Row_Untouched()
    {
        var export = Claimed();

        var result = export.MarkCompleted(OtherToken, "replacement-key", 5, Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(OrganisationDataExport.StatusInProgress, export.Status);
        Assert.Null(export.StorageKey);
        Assert.Equal(Token, export.LeaseOwnerToken);
    }

    [Fact]
    public void MarkFailed_From_Completed_Is_Conflict()
    {
        var export = Claimed();
        export.MarkCompleted(Token, "k", 1, Now);

        var result = export.MarkFailed("boom", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public void MarkFailed_From_InProgress_Uses_Default_Reason_When_Blank_And_Clears_Lease()
    {
        var export = Claimed();

        var result = export.MarkFailed("   ", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrganisationDataExport.StatusFailed, export.Status);
        Assert.Equal("Export could not be generated.", export.FailureReason);
        Assert.Null(export.LeaseOwnerToken);
        Assert.Null(export.LeaseAcquiredAt);
        Assert.Null(export.LeaseExpiresAt);
    }

    [Fact]
    public void MarkExpired_Only_From_Completed_And_Clears_Storage_Key()
    {
        var export = Claimed();
        export.MarkCompleted(Token, "k", 1, Now);

        var result = export.MarkExpired(Now.AddDays(8));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrganisationDataExport.StatusExpired, export.Status);
        Assert.Null(export.StorageKey);
    }

    [Fact]
    public void RecordDownload_Succeeds_While_Completed_And_Not_Expired()
    {
        var export = Claimed();
        export.MarkCompleted(Token, "k", 1, Now);

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
        var export = Claimed();
        export.MarkCompleted(Token, "k", 1, Now);

        var result = export.RecordDownload(Guid.NewGuid(), Now.AddDays(30));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    // ----- Ticket 3: recoverability / attempt tracking -----

    [Fact]
    public void Create_Starts_With_Version_One_And_Zero_Attempts()
    {
        var export = NewPending();

        Assert.Equal(1, export.Version);
        Assert.Equal(0, export.AttemptCount);
        Assert.Null(export.LastAttemptAt);
        Assert.True(export.CanAttemptAgain);
    }

    [Fact]
    public void IncrementVersion_Advances_Concurrency_Token()
    {
        var export = NewPending();
        export.IncrementVersion();
        Assert.Equal(2, export.Version);
    }

    [Fact]
    public void MarkInProgress_Also_Increments_Attempt_And_Stamps_LastAttemptAt()
    {
        var export = NewPending();

        export.MarkInProgress(Now.AddMinutes(1));

        Assert.Equal(1, export.AttemptCount);
        Assert.Equal(Now.AddMinutes(1), export.LastAttemptAt);
    }

    [Fact]
    public void BeginAttempt_From_Pending_Moves_To_InProgress_Counts_The_Attempt_And_Takes_A_Lease()
    {
        var export = NewPending();

        var result = export.BeginAttempt(Token, Now.AddMinutes(2));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrganisationDataExport.StatusInProgress, export.Status);
        Assert.Equal(1, export.AttemptCount);
        Assert.Equal(Now.AddMinutes(2), export.StartedAt);
        Assert.Equal(Now.AddMinutes(2), export.LastAttemptAt);
        Assert.Equal(Token, export.LeaseOwnerToken);
        Assert.Equal(Now.AddMinutes(2), export.LeaseAcquiredAt);
        Assert.Equal(Now.AddMinutes(2).AddMinutes(OrganisationDataExport.LeaseDurationMinutes), export.LeaseExpiresAt);
    }

    [Fact]
    public void BeginAttempt_With_Empty_Token_Is_Validation_Failure()
    {
        var export = NewPending();

        var result = export.BeginAttempt(Guid.Empty, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(OrganisationDataExport.StatusPending, export.Status);
    }

    [Fact]
    public void BeginAttempt_From_InProgress_With_A_Live_Lease_Is_Rejected_For_Another_Worker()
    {
        var export = Claimed();

        var result = export.BeginAttempt(OtherToken, Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(1, export.AttemptCount);
        Assert.Equal(Token, export.LeaseOwnerToken);
    }

    [Fact]
    public void BeginAttempt_From_InProgress_At_Exact_Lease_Expiry_Is_Allowed()
    {
        // IsLeaseExpired uses expires <= now, so the boundary instant counts as expired.
        var export = Claimed();
        var expiry = export.LeaseExpiresAt!.Value;

        var result = export.BeginAttempt(OtherToken, expiry);

        Assert.True(result.IsSuccess);
        Assert.Equal(OtherToken, export.LeaseOwnerToken);
    }

    [Fact]
    public void BeginAttempt_One_Tick_Before_Lease_Expiry_Is_Rejected()
    {
        var export = Claimed();
        var expiry = export.LeaseExpiresAt!.Value;

        var result = export.BeginAttempt(OtherToken, expiry.AddTicks(-1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public void BeginAttempt_From_InProgress_With_Expired_Lease_Hands_Ownership_To_The_New_Worker()
    {
        // First worker claimed 20 minutes ago and its 15-minute lease has expired.
        var export = Claimed(Now.AddMinutes(-20));

        var result = export.BeginAttempt(OtherToken, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrganisationDataExport.StatusInProgress, export.Status);
        Assert.Equal(2, export.AttemptCount);
        Assert.Equal(Now.AddMinutes(-20), export.StartedAt);
        Assert.Equal(Now, export.LastAttemptAt);
        Assert.Equal(OtherToken, export.LeaseOwnerToken);
        Assert.Equal(Now.AddMinutes(OrganisationDataExport.LeaseDurationMinutes), export.LeaseExpiresAt);
    }

    [Fact]
    public void BeginAttempt_At_Attempt_Limit_Is_Conflict_Even_With_An_Expired_Lease()
    {
        var export = NewPending();
        for (var i = 0; i < OrganisationDataExport.MaxAttempts; i++)
        {
            Assert.True(export.BeginAttempt(Token, Now.AddMinutes(-100 + i)).IsSuccess);
            if (i < OrganisationDataExport.MaxAttempts - 1)
                export.ResetForRetry(Now.AddMinutes(-100 + i));
        }

        Assert.Equal(OrganisationDataExport.MaxAttempts, export.AttemptCount);
        Assert.True(export.IsLeaseExpired(Now));

        var result = export.BeginAttempt(OtherToken, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Theory]
    [InlineData(OrganisationDataExport.StatusCompleted)]
    [InlineData(OrganisationDataExport.StatusFailed)]
    [InlineData(OrganisationDataExport.StatusExpired)]
    public void BeginAttempt_From_Terminal_State_Is_Conflict(string terminalStatus)
    {
        var export = ExportInStatus(terminalStatus);

        var result = export.BeginAttempt(Token, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public void IsLeaseExpired_Is_True_When_Never_Leased()
    {
        Assert.True(NewPending().IsLeaseExpired(Now));
    }

    // ----- Follow-up A: lease renewal (heartbeat) -----

    [Fact]
    public void RenewLease_By_Current_Owner_Pushes_Out_The_Expiry()
    {
        var export = Claimed();

        var result = export.RenewLease(Token, Now.AddMinutes(10));

        Assert.True(result.IsSuccess);
        Assert.Equal(Now.AddMinutes(10).AddMinutes(OrganisationDataExport.LeaseDurationMinutes), export.LeaseExpiresAt);
    }

    [Fact]
    public void RenewLease_By_Non_Owner_Is_Conflict()
    {
        var export = Claimed();

        var result = export.RenewLease(OtherToken, Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public void RenewLease_Fails_Once_Status_Left_InProgress()
    {
        var export = Claimed();
        export.MarkCompleted(Token, "k", 1, Now.AddMinutes(1));

        var result = export.RenewLease(Token, Now.AddMinutes(2));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public void ResetForRetry_From_InProgress_Returns_To_Pending_Clears_StartedAt_And_Lease()
    {
        var export = Claimed();

        var result = export.ResetForRetry(Now.AddMinutes(40));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrganisationDataExport.StatusPending, export.Status);
        Assert.Null(export.StartedAt);
        Assert.Null(export.LeaseOwnerToken);
        Assert.Null(export.LeaseAcquiredAt);
        Assert.Null(export.LeaseExpiresAt);
        // attempt history is preserved so the sweep can still give up after MaxAttempts
        Assert.Equal(1, export.AttemptCount);
    }

    [Theory]
    [InlineData(OrganisationDataExport.StatusPending)]
    [InlineData(OrganisationDataExport.StatusCompleted)]
    [InlineData(OrganisationDataExport.StatusFailed)]
    [InlineData(OrganisationDataExport.StatusExpired)]
    public void ResetForRetry_From_Non_InProgress_Is_Conflict(string status)
    {
        var export = ExportInStatus(status);

        var result = export.ResetForRetry(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public void MarkFailedDueToMissingDocuments_Sets_Failed_Count_Reason_CompletedAt_And_Clears_Lease()
    {
        var export = Claimed();

        var result = export.MarkFailedDueToMissingDocuments(Token, 3, Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrganisationDataExport.StatusFailed, export.Status);
        Assert.Equal(3, export.MissingDocumentCount);
        Assert.Equal(Now.AddMinutes(1), export.CompletedAt);
        Assert.Contains("3 expected documents", export.FailureReason);
        Assert.Null(export.LeaseOwnerToken);
    }

    [Fact]
    public void MarkFailedDueToMissingDocuments_With_Stale_Token_Is_A_No_Op_Conflict()
    {
        var export = Claimed();

        var result = export.MarkFailedDueToMissingDocuments(OtherToken, 2, Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(OrganisationDataExport.StatusInProgress, export.Status);
        Assert.Equal(0, export.MissingDocumentCount);
        Assert.Equal(Token, export.LeaseOwnerToken);
    }

    [Fact]
    public void MarkFailedDueToMissingDocuments_Uses_Singular_Reason_For_One_And_Floors_Negative()
    {
        var export = Claimed();
        Assert.True(export.MarkFailedDueToMissingDocuments(Token, 1, Now).IsSuccess);
        Assert.Equal("1 expected document could not be retrieved from storage.", export.FailureReason);

        var negative = Claimed();
        negative.MarkFailedDueToMissingDocuments(Token, -5, Now);
        Assert.Equal(0, negative.MissingDocumentCount);
    }

    [Theory]
    [InlineData(OrganisationDataExport.StatusCompleted)]
    [InlineData(OrganisationDataExport.StatusExpired)]
    public void MarkFailedDueToMissingDocuments_From_Completed_Or_Expired_Is_Conflict(string status)
    {
        var export = ExportInStatus(status);

        var result = export.MarkFailedDueToMissingDocuments(Token, 1, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public void CanAttemptAgain_Is_False_Once_AttemptCount_Reaches_MaxAttempts()
    {
        var export = NewPending();

        for (var i = 1; i < OrganisationDataExport.MaxAttempts; i++)
        {
            export.BeginAttempt(Token, Now);
            Assert.Equal(i, export.AttemptCount);
            Assert.True(export.CanAttemptAgain, $"still recoverable after attempt {i}");
            export.ResetForRetry(Now);
        }

        export.BeginAttempt(Token, Now); // the MaxAttempts-th attempt
        Assert.Equal(OrganisationDataExport.MaxAttempts, export.AttemptCount);
        Assert.False(export.CanAttemptAgain);
    }

    // ----- Follow-up D: ownership-guarded MarkFailed(ownerToken, ...) -----

    [Fact]
    public void MarkFailed_With_Owner_Token_Rejected_When_Superseded()
    {
        var export = Claimed();

        var result = export.MarkFailed(OtherToken, "upload blew up", Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(OrganisationDataExport.StatusInProgress, export.Status);
        Assert.Equal(Token, export.LeaseOwnerToken);
        Assert.Null(export.FailureReason);
    }

    [Fact]
    public void MarkFailed_With_Owner_Token_Succeeds_For_Current_Owner()
    {
        var export = Claimed();

        var result = export.MarkFailed(Token, "upload blew up", Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrganisationDataExport.StatusFailed, export.Status);
        Assert.Equal("upload blew up", export.FailureReason);
        Assert.Equal(Now.AddMinutes(1), export.CompletedAt);
        Assert.Null(export.LeaseOwnerToken);
        Assert.Null(export.LeaseAcquiredAt);
        Assert.Null(export.LeaseExpiresAt);
    }

    [Fact]
    public void MarkFailed_With_Owner_Token_Rejected_When_Not_InProgress()
    {
        // Ticket 3J: a worker-owned failure requires this worker to still hold the live lease. A
        // Pending row (e.g. after a recovery reset) can no longer be failed via the owner-token path
        // — a superseded worker is locked out, including during the post-recovery Pending window.
        var export = NewPending();

        var result = export.MarkFailed(OtherToken, "gave up before starting", Now);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(OrganisationDataExport.StatusPending, export.Status);
    }

    // ----- Follow-up H: recovery claim / reset -----

    [Fact]
    public void ClaimForRecovery_With_Empty_Token_Is_Validation_Failure()
    {
        var export = Claimed(Now.AddMinutes(-20));

        var result = export.ClaimForRecovery(Guid.Empty, Now);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Theory]
    [InlineData(OrganisationDataExport.StatusPending)]
    [InlineData(OrganisationDataExport.StatusCompleted)]
    [InlineData(OrganisationDataExport.StatusFailed)]
    [InlineData(OrganisationDataExport.StatusExpired)]
    public void ClaimForRecovery_From_Non_InProgress_Is_Conflict(string status)
    {
        var export = ExportInStatus(status);

        var result = export.ClaimForRecovery(OtherToken, Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public void ClaimForRecovery_While_Lease_Is_Still_Live_Is_Conflict()
    {
        var export = Claimed(); // lease live for 15 minutes

        var result = export.ClaimForRecovery(OtherToken, Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(Token, export.LeaseOwnerToken);
    }

    [Fact]
    public void ClaimForRecovery_At_Exact_Lease_Expiry_Succeeds_And_Takes_A_Fresh_Lease()
    {
        var export = Claimed();
        var expiry = export.LeaseExpiresAt!.Value;

        var result = export.ClaimForRecovery(OtherToken, expiry);

        Assert.True(result.IsSuccess);
        Assert.Equal(OtherToken, export.LeaseOwnerToken);
        Assert.Equal(expiry, export.LeaseAcquiredAt);
        Assert.Equal(expiry.AddMinutes(OrganisationDataExport.LeaseDurationMinutes), export.LeaseExpiresAt);
        Assert.Equal(OrganisationDataExport.StatusInProgress, export.Status);
    }

    [Fact]
    public void ClaimForRecovery_One_Tick_Before_Lease_Expiry_Is_Rejected()
    {
        var export = Claimed();
        var expiry = export.LeaseExpiresAt!.Value;

        var result = export.ClaimForRecovery(OtherToken, expiry.AddTicks(-1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public void ResetForRetry_With_Recovery_Token_Rejected_On_Token_Mismatch()
    {
        var export = Claimed(Now.AddMinutes(-20));
        Assert.True(export.ClaimForRecovery(OtherToken, Now).IsSuccess);

        // A different recovery sweep (or the resurrected original worker) cannot reset it.
        var result = export.ResetForRetry(Token, Now.AddMinutes(1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(OrganisationDataExport.StatusInProgress, export.Status);
        Assert.Equal(OtherToken, export.LeaseOwnerToken);
    }

    [Fact]
    public void ResetForRetry_With_Recovery_Token_Succeeds_For_The_Claiming_Sweep()
    {
        var export = Claimed(Now.AddMinutes(-20));
        Assert.True(export.ClaimForRecovery(OtherToken, Now).IsSuccess);

        var result = export.ResetForRetry(OtherToken, Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(OrganisationDataExport.StatusPending, export.Status);
        Assert.Null(export.StartedAt);
        Assert.Null(export.LeaseOwnerToken);
        Assert.Null(export.LeaseExpiresAt);
    }

    [Theory]
    [InlineData(OrganisationDataExport.StatusPending)]
    [InlineData(OrganisationDataExport.StatusCompleted)]
    [InlineData(OrganisationDataExport.StatusFailed)]
    [InlineData(OrganisationDataExport.StatusExpired)]
    public void ResetForRetry_With_Recovery_Token_From_Non_InProgress_Is_Conflict(string status)
    {
        var export = ExportInStatus(status);

        var result = export.ResetForRetry(OtherToken, Now.AddDays(1));

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    // ----- Follow-up I: attempt-file cleanup marker -----

    [Theory]
    [InlineData(OrganisationDataExport.StatusPending)]
    [InlineData(OrganisationDataExport.StatusInProgress)]
    public void MarkAttemptFilesCleaned_From_Non_Terminal_State_Is_Conflict(string status)
    {
        var export = ExportInStatus(status);

        var result = export.MarkAttemptFilesCleaned(Now);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Null(export.AttemptFilesCleanedAt);
    }

    [Theory]
    [InlineData(OrganisationDataExport.StatusCompleted)]
    [InlineData(OrganisationDataExport.StatusFailed)]
    [InlineData(OrganisationDataExport.StatusExpired)]
    public void MarkAttemptFilesCleaned_From_Terminal_State_Stamps_The_Timestamp(string status)
    {
        var export = ExportInStatus(status);
        var when = Now.AddDays(2);

        var result = export.MarkAttemptFilesCleaned(when);

        Assert.True(result.IsSuccess);
        Assert.Equal(when, export.AttemptFilesCleanedAt);
    }

    [Fact]
    public void Create_Starts_With_No_AttemptFilesCleanedAt_And_Is_Not_Terminal()
    {
        var export = NewPending();

        Assert.Null(export.AttemptFilesCleanedAt);
        Assert.False(export.IsTerminal);
    }

    private static OrganisationDataExport ExportInStatus(string status)
    {
        var export = NewPending();
        switch (status)
        {
            case OrganisationDataExport.StatusPending:
                return export;
            case OrganisationDataExport.StatusInProgress:
                export.BeginAttempt(Token, Now);
                return export;
            case OrganisationDataExport.StatusCompleted:
                export.BeginAttempt(Token, Now);
                export.MarkCompleted(Token, "k", 1, Now);
                return export;
            case OrganisationDataExport.StatusFailed:
                export.BeginAttempt(Token, Now);
                export.MarkFailed("boom", Now);
                return export;
            case OrganisationDataExport.StatusExpired:
                export.BeginAttempt(Token, Now);
                export.MarkCompleted(Token, "k", 1, Now);
                export.MarkExpired(Now.AddDays(30));
                return export;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }
}
