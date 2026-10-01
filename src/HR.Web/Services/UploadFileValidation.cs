using Microsoft.AspNetCore.Components.Forms;

namespace HR.Web.Services;

public static class UploadFileValidation
{
    public static readonly string[] DocumentExtensions = [".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png"];
    public const string DocumentTypeDescription = "PDF, Word (DOC or DOCX), JPG or PNG file";
    public const long DocumentMaxBytes = 20 * 1024 * 1024;

    public static string? Validate(IBrowserFile file, IReadOnlyCollection<string> allowedExtensions, string typeDescription, long maxBytes)
    {
        var extension = Path.GetExtension(file.Name);
        if (!allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return $"The file must be a {typeDescription}.";
        if (file.Size == 0)
            return "The selected file is empty.";
        if (file.Size > maxBytes)
            return $"The file is larger than the {maxBytes / (1024 * 1024)} MB limit.";
        return null;
    }

    public static readonly string[] SupportExtensions = [".pdf", ".png", ".jpg", ".jpeg", ".txt", ".log"];
    public const string SupportTypeDescription = "PDF, PNG, JPG, TXT or LOG file";
    public const long SupportMaxBytes = 10 * 1024 * 1024;
    public const int SupportMaxFiles = 5;

    public static string? ValidateSupportAttachment(IBrowserFile file) =>
        Validate(file, SupportExtensions, SupportTypeDescription, SupportMaxBytes);

    public static string? ValidateDocument(IBrowserFile file) =>
        Validate(file, DocumentExtensions, DocumentTypeDescription, DocumentMaxBytes);
}
