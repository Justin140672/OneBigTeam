using HR.Modules.Support.Services;

namespace HR.Modules.Support.Tests;

/// <summary>
/// Security review ticket 4 (P1): the shared support-attachment upload policy — aggregate limits,
/// per-file size/extension/content-type allow-list, and magic-byte signature verification.
/// </summary>
public class SupportAttachmentValidatorTests
{
    private readonly SupportAttachmentValidator _validator = new();

    [Fact]
    public void ValidateAggregate_Rejects_Too_Many_Files()
    {
        var result = _validator.ValidateAggregate(SupportAttachmentPolicy.MaxFileCount + 1, 1024);

        Assert.True(result.IsFailure);
        Assert.Contains("No more than", result.Error.Message);
    }

    [Fact]
    public void ValidateAggregate_Rejects_Total_Size_Over_Limit()
    {
        var result = _validator.ValidateAggregate(1, SupportAttachmentPolicy.MaxTotalSizeBytes + 1);

        Assert.True(result.IsFailure);
        Assert.Contains("combined size", result.Error.Message);
    }

    [Fact]
    public void ValidateAggregate_Accepts_Within_Limits()
    {
        var result = _validator.ValidateAggregate(SupportAttachmentPolicy.MaxFileCount, SupportAttachmentPolicy.MaxTotalSizeBytes);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateFile_Rejects_Oversized_File()
    {
        var result = _validator.ValidateFile("photo.png", "image/png", SupportAttachmentPolicy.MaxFileSizeBytes + 1);

        Assert.True(result.IsFailure);
        Assert.Contains("exceeds the maximum allowed size", result.Error.Message);
    }

    [Fact]
    public void ValidateFile_Rejects_Empty_File()
    {
        var result = _validator.ValidateFile("photo.png", "image/png", 0);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("script.js")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    public void ValidateFile_Rejects_Disallowed_Extension(string fileName)
    {
        var result = _validator.ValidateFile(fileName, "image/png", 1024);

        Assert.True(result.IsFailure);
        Assert.Contains("is not allowed", result.Error.Message);
    }

    [Fact]
    public void ValidateFile_Rejects_Extension_ContentType_Mismatch()
    {
        // A .png extension is on the allow-list, but "application/zip" is not an allowed content
        // type at all — must be rejected regardless of the extension.
        var result = _validator.ValidateFile("photo.png", "application/zip", 1024);

        Assert.True(result.IsFailure);
        Assert.Contains("Content type", result.Error.Message);
    }

    [Theory]
    [InlineData("../../etc/passwd.png")]
    [InlineData("..\\..\\windows\\system32\\evil.png")]
    [InlineData("con.png")]
    public void ValidateFile_Does_Not_Throw_On_Malicious_FileName(string maliciousFileName)
    {
        // The validator itself must never throw on a hostile file name (path traversal, reserved
        // Windows device names, etc.) — path-safety is enforced by using a GUID-based storage key
        // elsewhere (SubmitSupportRequestHandler / SupabaseSupportAttachmentStorageService), not by
        // this validator, but a crash here would still be a denial-of-service bug.
        var exception = Record.Exception(() => _validator.ValidateFile(maliciousFileName, "image/png", 1024));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateContentSignature_Accepts_Matching_Png_Header()
    {
        var content = new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x00, 0x00]);

        var result = _validator.ValidateContentSignature(content, "image/png");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateContentSignature_Rejects_Mismatched_Header()
    {
        // Declares image/png but the bytes are a plain text file — the classic "renamed executable"
        // attack this check exists to catch.
        var content = new MemoryStream("not a real png"u8.ToArray());

        var result = _validator.ValidateContentSignature(content, "image/png");

        Assert.True(result.IsFailure);
        Assert.Contains("does not match the declared type", result.Error.Message);
    }

    [Fact]
    public void ValidateContentSignature_Rewinds_Stream_After_Check()
    {
        var content = new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x00, 0x00]);
        content.Position = 0;

        _validator.ValidateContentSignature(content, "image/png");

        Assert.Equal(0, content.Position);
    }

    [Fact]
    public void ValidateContentSignature_Allows_Types_With_No_Known_Signature()
    {
        var content = new MemoryStream("plain text log content"u8.ToArray());

        var result = _validator.ValidateContentSignature(content, "text/plain");

        Assert.True(result.IsSuccess);
    }
}
