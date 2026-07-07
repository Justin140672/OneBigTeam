using HR.Modules.DataImport.Services;
using Microsoft.Extensions.Options;

namespace HR.Modules.DataImport.Tests;

public class ImportFileValidatorTests
{
    private static ImportFileValidator CreateValidator(Action<ImportFileUploadOptions>? configure = null)
    {
        var options = new ImportFileUploadOptions();
        configure?.Invoke(options);
        return new ImportFileValidator(Options.Create(options));
    }

    [Fact]
    public void Validate_ValidCsv_ReturnsSuccess()
    {
        var validator = CreateValidator();

        var result = validator.Validate("employees.csv", "text/csv", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_ValidXlsx_ReturnsSuccess()
    {
        var validator = CreateValidator();

        var result = validator.Validate(
            "employees.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            2048);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_CsvWithBrowserExcelContentType_ReturnsSuccess()
    {
        var validator = CreateValidator();

        // Some browsers send this MIME type for CSV uploads.
        var result = validator.Validate("employees.csv", "application/vnd.ms-excel", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_EmptyFile_ReturnsFailure()
    {
        var validator = CreateValidator();

        var result = validator.Validate("employees.csv", "text/csv", 0);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("empty", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_FileTooLarge_ReturnsFailure()
    {
        var validator = CreateValidator(o => o.MaxFileSizeBytes = 1024);

        var result = validator.Validate("employees.csv", "text/csv", 2048);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("size", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_FileSizeAtLimit_ReturnsSuccess()
    {
        var validator = CreateValidator(o => o.MaxFileSizeBytes = 1024);

        var result = validator.Validate("employees.csv", "text/csv", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_DisallowedExtension_ReturnsFailure()
    {
        var validator = CreateValidator();

        var result = validator.Validate("employees.txt", "text/plain", 1024);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains(".txt", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_NoExtension_ReturnsFailure()
    {
        var validator = CreateValidator();

        var result = validator.Validate("employees", "text/csv", 1024);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Validate_ExtensionIsCaseInsensitive_ReturnsSuccess()
    {
        var validator = CreateValidator();

        var result = validator.Validate("EMPLOYEES.CSV", "text/csv", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_DisallowedContentType_ReturnsFailure()
    {
        var validator = CreateValidator();

        // Extension is allowed but content type is not one of the accepted values.
        var result = validator.Validate("employees.csv", "application/json", 1024);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("application/json", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ContentTypeWithParameters_Succeeds()
    {
        var validator = CreateValidator();

        var result = validator.Validate("employees.csv", "text/csv; charset=utf-8", 1024);

        Assert.True(result.IsSuccess);
    }

    // --- ValidateContent (magic bytes) ---

    private static Stream XlsxZipStream() => new MemoryStream([0x50, 0x4B, 0x03, 0x04, 0x00]); // PK zip
    private static Stream ZeroStream()    => new MemoryStream([0x00, 0x00, 0x00, 0x00, 0x00]);
    private static Stream CsvTextStream() => new MemoryStream("a,b,c\n1,2,3"u8.ToArray());

    [Fact]
    public void ValidateContent_Xlsx_WithCorrectMagicBytes_ReturnsSuccess()
    {
        var result = CreateValidator().ValidateContent(
            XlsxZipStream(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateContent_Xlsx_WithWrongMagicBytes_ReturnsFailure()
    {
        // File claims to be XLSX but the bytes are not a ZIP/OOXML container (spoofed/renamed).
        var result = CreateValidator().ValidateContent(
            ZeroStream(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("content does not match", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateContent_Csv_HasNoKnownSignature_AlwaysReturnsSuccess()
    {
        // CSV is plain text with no reliable magic byte, so it always defers to other checks.
        var result = CreateValidator().ValidateContent(CsvTextStream(), "text/csv");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateContent_ContentTypeWithParameters_MatchesCorrectly()
    {
        var result = CreateValidator().ValidateContent(
            XlsxZipStream(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet; charset=utf-8");

        Assert.True(result.IsSuccess);
    }
}
