using HR.Modules.Documents.Services;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Tests;

public class FileUploadValidatorTests
{
    private static FileUploadValidator CreateValidator(Action<FileUploadOptions>? configure = null)
    {
        var options = new FileUploadOptions();
        configure?.Invoke(options);
        return new FileUploadValidator(Options.Create(options));
    }

    [Fact]
    public void Validate_ValidPdf_ReturnsSuccess()
    {
        var validator = CreateValidator();

        var result = validator.Validate("report.pdf", "application/pdf", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_ValidDocx_ReturnsSuccess()
    {
        var validator = CreateValidator();

        var result = validator.Validate(
            "contract.docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            512_000);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_ValidJpeg_ReturnsSuccess()
    {
        var validator = CreateValidator();

        var result = validator.Validate("photo.jpg", "image/jpeg", 200_000);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_EmptyFile_ReturnsFailure()
    {
        var validator = CreateValidator();

        var result = validator.Validate("report.pdf", "application/pdf", 0);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("empty", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_FileTooLarge_ReturnsFailure()
    {
        var validator = CreateValidator(o => o.MaxFileSizeBytes = 1024);

        var result = validator.Validate("report.pdf", "application/pdf", 2048);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("size", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_FileSizeAtLimit_ReturnsSuccess()
    {
        var validator = CreateValidator(o => o.MaxFileSizeBytes = 1024);

        var result = validator.Validate("report.pdf", "application/pdf", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_DisallowedExtension_ReturnsFailure()
    {
        var validator = CreateValidator();

        var result = validator.Validate("script.exe", "application/octet-stream", 1024);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains(".exe", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_NoExtension_ReturnsFailure()
    {
        var validator = CreateValidator();

        var result = validator.Validate("filename", "application/pdf", 1024);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Validate_ExtensionIsCaseInsensitive_ReturnsSuccess()
    {
        var validator = CreateValidator();

        var result = validator.Validate("REPORT.PDF", "application/pdf", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_DisallowedContentType_ReturnsFailure()
    {
        var validator = CreateValidator();

        // Extension is allowed but content type is spoofed
        var result = validator.Validate("report.pdf", "text/html", 1024);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("text/html", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ContentTypeWithParameters_Succeeds()
    {
        var validator = CreateValidator();

        // Some clients append charset or boundary parameters
        var result = validator.Validate("report.pdf", "application/pdf; charset=utf-8", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_ContentTypeIsCaseInsensitive_ReturnsSuccess()
    {
        var validator = CreateValidator();

        var result = validator.Validate("photo.jpg", "Image/JPEG", 1024);

        Assert.True(result.IsSuccess);
    }

    // --- ValidateContent (magic bytes) ---

    private static Stream PdfStream()    => new MemoryStream([0x25, 0x50, 0x44, 0x46, 0x2D]); // %PDF-
    private static Stream JpegStream()   => new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 0x00]); // JFIF
    private static Stream PngStream()    => new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x00]); // PNG
    private static Stream DocxStream()   => new MemoryStream([0x50, 0x4B, 0x03, 0x04, 0x00]); // PK zip
    private static Stream ZeroStream()   => new MemoryStream([0x00, 0x00, 0x00, 0x00, 0x00]);

    [Fact]
    public void ValidateContent_Pdf_WithCorrectMagicBytes_ReturnsSuccess()
    {
        var result = CreateValidator().ValidateContent(PdfStream(), "application/pdf");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateContent_Jpeg_WithCorrectMagicBytes_ReturnsSuccess()
    {
        var result = CreateValidator().ValidateContent(JpegStream(), "image/jpeg");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateContent_Png_WithCorrectMagicBytes_ReturnsSuccess()
    {
        var result = CreateValidator().ValidateContent(PngStream(), "image/png");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateContent_Docx_WithCorrectMagicBytes_ReturnsSuccess()
    {
        var result = CreateValidator().ValidateContent(DocxStream(), "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateContent_Pdf_WithWrongMagicBytes_ReturnsFailure()
    {
        // File claims to be PDF but has zero bytes (spoofed/renamed)
        var result = CreateValidator().ValidateContent(ZeroStream(), "application/pdf");
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("content does not match", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateContent_UnknownContentType_ReturnsSuccess()
    {
        // No magic bytes defined for unknown types — let other checks handle it
        var result = CreateValidator().ValidateContent(ZeroStream(), "application/octet-stream");
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateContent_ContentTypeWithParameters_MatchesCorrectly()
    {
        // Ensure params like "; charset=utf-8" are stripped before lookup
        var result = CreateValidator().ValidateContent(PdfStream(), "application/pdf; charset=utf-8");
        Assert.True(result.IsSuccess);
    }
}
