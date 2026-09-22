namespace HR.Modules.DataImport.Services;

/// <summary>
/// Security review finding #2: configurable retention for the durable raw import workbook, which
/// contains PII (names, emails, employment data, manager relationships). The workbook is only
/// ever read by ValidateImportSession — ConfirmImportSession works entirely from the staging
/// rows persisted at validation time — so the file is deleted immediately once validation
/// finishes (success, validation-with-errors, or a read failure), with this options class only
/// providing the safety net retained for sessions that never reach that point (e.g. a session
/// abandoned before it was ever validated) and a small grace window before the sweep will
/// re-attempt a delete that failed inline.
/// </summary>
internal sealed class DataImportFileRetentionOptions
{
    /// <summary>
    /// How long a session may sit in Pending/Processing without validating before its file is
    /// considered abandoned and eligible for the sweep job to delete. Chosen to comfortably
    /// exceed how long an upload-to-validate workflow should ever legitimately take in a single
    /// browser session, while still purging PII within a reasonable window.
    /// </summary>
    public int AbandonedSessionRetentionDays { get; set; } = 7;

    /// <summary>
    /// Grace period after a session reaches a terminal state (or after an inline deletion
    /// attempt failed) before the sweep job retries deleting its file. Non-zero mainly to avoid
    /// hammering storage immediately after an inline failure and to give an operator a short
    /// window to investigate an unusual failure before the next automated attempt.
    /// </summary>
    public int RetryGraceHours { get; set; } = 1;

    /// <summary>
    /// Number of failed deletion attempts (inline + sweep combined) after which the sweep job
    /// logs an exhausted-attempts error so it can be picked up by log-based alerting, without
    /// inventing new alerting infrastructure.
    /// </summary>
    public int ExhaustedAttemptThreshold { get; set; } = 5;

    /// <summary>
    /// Follow-up review finding: how long a durable pre-upload "upload intent"
    /// (<see cref="HR.Modules.DataImport.Domain.OrphanedImportFileUpload"/> with no
    /// <c>ConfirmedAt</c>) may remain unresolved before the reconciliation sweep treats it as
    /// abandoned and checks storage directly. Kept comfortably above how long a single
    /// upload-plus-session-save request should ever legitimately take, so an in-flight request is
    /// never raced by the sweep.
    /// </summary>
    public int UploadIntentGracePeriodMinutes { get; set; } = 60;
}
