namespace HR.Modules.Support.Services;

/// <summary>
/// Security review ticket 4 (P1): the shared upload policy for support-request and
/// support-response attachments — max file count, per-file size, aggregate size, and the
/// extension/content-type allow-list. Mirrors the shape of
/// HR.Modules.Documents.Services.FileUploadOptions (that module's equivalent policy for document
/// uploads) but is intentionally its own copy in this module: modules must not reference each
/// other's projects, and this policy is deliberately simpler (support attachments are
/// screenshots/small logs/short documents, not the wider set of HR document types).
/// </summary>
internal static class SupportAttachmentPolicy
{
    public const int MaxFileCount = 5;
    public const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB per file
    public const long MaxTotalSizeBytes = 25 * 1024 * 1024; // 25 MB per request/response

    public static readonly IReadOnlyCollection<string> AllowedExtensions =
    [
        ".pdf",
        ".png",
        ".jpg",
        ".jpeg",
        ".txt",
        ".log",
    ];

    public static readonly IReadOnlyCollection<string> AllowedContentTypes =
    [
        "application/pdf",
        "image/png",
        "image/jpeg",
        "text/plain",
    ];

    // Maps a declared content type to the magic byte sequences that identify it — the same
    // signature-verification approach as HR.Modules.Documents.Services.FileUploadValidator.
    // "text/plain"/.log have no reliable magic bytes and are only gated by extension/MIME/size.
    public static readonly IReadOnlyDictionary<string, byte[][]> MagicBytes =
        new Dictionary<string, byte[][]>(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = [[0x25, 0x50, 0x44, 0x46]], // %PDF
            ["image/png"] = [[0x89, 0x50, 0x4E, 0x47]], // .PNG
            ["image/jpeg"] =
            [
                [0xFF, 0xD8, 0xFF, 0xE0],
                [0xFF, 0xD8, 0xFF, 0xE1],
                [0xFF, 0xD8, 0xFF, 0xE8],
                [0xFF, 0xD8, 0xFF, 0xDB],
            ],
        };
}
