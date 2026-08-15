namespace HR.Modules.DataImport.Services;

internal sealed class ImportFileUploadOptions
{
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB

    public List<string> AllowedExtensions { get; set; } =
    [
        ".xlsx",
    ];

    public List<string> AllowedContentTypes { get; set; } =
    [
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    ];
}
