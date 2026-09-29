namespace HR.Modules.DataImport.Services;

internal sealed class DataImportFileRetentionOptions
{
    public int AbandonedSessionRetentionDays { get; set; } = 7;

    /// <summary>
    /// Grace period after a session reaches a terminal state (or after an inline deletion
    /// attempt failed) before the sweep job retries deleting its file. Non-zero mainly to avoid
    /// hammering storage immediately after an inline failure and to give an operator a short
    /// window to investigate an unusual failure before the next automated attempt.
    /// </summary>
    public int RetryGraceHours { get; set; } = 1;

    public int ExhaustedAttemptThreshold { get; set; } = 5;

    public int UploadIntentGracePeriodMinutes { get; set; } = 60;
}
