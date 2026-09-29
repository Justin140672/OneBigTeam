namespace HR.Modules.Documents.Services;

internal sealed class FileUploadOptions
{
    public long MaxFileSizeBytes { get; set; } = 20 * 1024 * 1024;

    public List<string> AllowedExtensions { get; set; } =
    [
        ".pdf",
        ".doc",
        ".docx",
        ".jpg",
        ".jpeg",
        ".png",
    ];

    public List<string> AllowedContentTypes { get; set; } =
    [
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "image/jpeg",
        "image/png",
    ];
}
