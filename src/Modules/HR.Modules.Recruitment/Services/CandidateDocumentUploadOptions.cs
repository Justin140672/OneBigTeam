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
}
