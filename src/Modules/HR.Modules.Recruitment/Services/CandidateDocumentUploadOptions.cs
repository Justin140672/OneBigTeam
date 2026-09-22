namespace HR.Modules.Recruitment.Services;

internal sealed class CandidateDocumentUploadOptions
{
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024; // 20 MB

    public List<string> AllowedExtensions { get; set; } =
    [
        ".pdf",
        ".doc",
        ".docx",
    ];

    public List<string> AllowedContentTypes { get; set; } =
    [
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    ];

    /// <summary>
    /// Follow-up review finding: how long a durable pre-upload "upload intent"
    /// (<see cref="HR.Modules.Recruitment.Domain.CandidateDocumentDeletionOperation"/>, status
    /// Reserved) may sit unconfirmed before PurgeCandidateDocumentStorageReconciliationJob treats it
    /// as abandoned and resolves it (checks whether the object exists in storage and either hands it
    /// to the delete pipeline or clears it). Mirrors
    /// DataImportFileRetentionOptions.UploadIntentGracePeriodMinutes.
    /// </summary>
    public int UploadIntentGracePeriodMinutes { get; set; } = 60;
}
