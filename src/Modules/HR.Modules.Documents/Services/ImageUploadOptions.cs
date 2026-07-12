namespace HR.Modules.Documents.Services;

internal sealed class ImageUploadOptions
{
    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024; // 5 MB

    public List<string> AllowedExtensions { get; set; } =
    [
        ".jpg",
        ".jpeg",
        ".png",
    ];

    public List<string> AllowedContentTypes { get; set; } =
    [
        "image/jpeg",
        "image/png",
    ];

    public int MinWidthPx { get; set; } = 100;

    public int MinHeightPx { get; set; } = 100;

    public int MaxWidthPx { get; set; } = 4000;

    public int MaxHeightPx { get; set; } = 4000;
}
