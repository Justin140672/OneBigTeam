using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using Microsoft.Extensions.Options;

namespace HR.Modules.Documents.Tests;

public class ImageUploadValidatorTests
{
    private static ImageUploadValidator CreateValidator(Action<ImageUploadOptions>? configure = null)
    {
        var options = new ImageUploadOptions();
        configure?.Invoke(options);
        return new ImageUploadValidator(Options.Create(options));
    }

    // --- Validate (metadata: extension/content-type/size) ---

    [Fact]
    public void Validate_ValidJpeg_ReturnsSuccess()
    {
        var result = CreateValidator().Validate("photo.jpg", "image/jpeg", 200_000);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_ValidPng_ReturnsSuccess()
    {
        var result = CreateValidator().Validate("photo.png", "image/png", 200_000);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_EmptyFile_ReturnsFailure()
    {
        var result = CreateValidator().Validate("photo.png", "image/png", 0);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("empty", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_NegativeSize_ReturnsFailure()
    {
        var result = CreateValidator().Validate("photo.png", "image/png", -1);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Validate_FileTooLarge_ReturnsFailure()
    {
        var validator = CreateValidator(o => o.MaxFileSizeBytes = 1024);

        var result = validator.Validate("photo.png", "image/png", 2048);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("size", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_FileSizeAtLimit_ReturnsSuccess()
    {
        var validator = CreateValidator(o => o.MaxFileSizeBytes = 1024);

        var result = validator.Validate("photo.png", "image/png", 1024);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_DisallowedExtension_ReturnsFailure()
    {
        var result = CreateValidator().Validate("photo.gif", "image/gif", 1024);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains(".gif", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_DisallowedContentType_ReturnsFailure()
    {
        // Extension is allowed but content type is spoofed.
        var result = CreateValidator().Validate("photo.png", "text/html", 1024);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("text/html", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ExtensionIsCaseInsensitive_ReturnsSuccess()
    {
        var result = CreateValidator().Validate("PHOTO.PNG", "image/png", 1024);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Validate_ContentTypeWithParameters_Succeeds()
    {
        var result = CreateValidator().Validate("photo.jpg", "image/jpeg; charset=utf-8", 1024);
        Assert.True(result.IsSuccess);
    }

    // --- ValidateImageContent (magic bytes + dimensions) ---

    [Fact]
    public void ValidateImageContent_ValidPng_WithinBounds_ReturnsSuccess()
    {
        var content = new MemoryStream(ImageTestBytes.BuildPng(400, 300));

        var result = CreateValidator().ValidateImageContent(content, "image/png");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateImageContent_ValidJpeg_WithinBounds_ReturnsSuccess()
    {
        var content = new MemoryStream(ImageTestBytes.BuildJpeg(400, 300));

        var result = CreateValidator().ValidateImageContent(content, "image/jpeg");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateImageContent_Png_AtMinimumBounds_ReturnsSuccess()
    {
        var validator = CreateValidator(o => { o.MinWidthPx = 100; o.MinHeightPx = 100; });
        var content   = new MemoryStream(ImageTestBytes.BuildPng(100, 100));

        var result = validator.ValidateImageContent(content, "image/png");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateImageContent_Png_AtMaximumBounds_ReturnsSuccess()
    {
        var validator = CreateValidator(o => { o.MaxWidthPx = 4000; o.MaxHeightPx = 4000; });
        var content   = new MemoryStream(ImageTestBytes.BuildPng(4000, 4000));

        var result = validator.ValidateImageContent(content, "image/png");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateImageContent_Png_SpoofedContent_ReturnsFailure()
    {
        // Declares PNG but the bytes are just zeros (renamed/tampered file).
        var content = new MemoryStream([0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);

        var result = CreateValidator().ValidateImageContent(content, "image/png");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("content does not match", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateImageContent_Jpeg_SpoofedContent_ReturnsFailure()
    {
        // Declares JPEG but the bytes are actually a PNG signature.
        var content = new MemoryStream(ImageTestBytes.BuildPng(400, 300));

        var result = CreateValidator().ValidateImageContent(content, "image/jpeg");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("content does not match", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateImageContent_TooShortToBeAnImage_ReturnsFailure()
    {
        var content = new MemoryStream([0x89, 0x50, 0x4E]); // fewer than 8 bytes

        var result = CreateValidator().ValidateImageContent(content, "image/png");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void ValidateImageContent_UnsupportedContentType_ReturnsFailure()
    {
        var content = new MemoryStream(ImageTestBytes.BuildPng(400, 300));

        var result = CreateValidator().ValidateImageContent(content, "image/gif");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("image/gif", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateImageContent_ContentTypeWithParameters_MatchesCorrectly()
    {
        var content = new MemoryStream(ImageTestBytes.BuildPng(400, 300));

        var result = CreateValidator().ValidateImageContent(content, "image/png; charset=utf-8");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ValidateImageContent_Png_BelowMinimumDimensions_ReturnsFailure()
    {
        var validator = CreateValidator(o => { o.MinWidthPx = 100; o.MinHeightPx = 100; });
        var content   = new MemoryStream(ImageTestBytes.BuildPng(50, 50));

        var result = validator.ValidateImageContent(content, "image/png");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("smaller than the minimum", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateImageContent_Png_AboveMaximumDimensions_ReturnsFailure()
    {
        var validator = CreateValidator(o => { o.MaxWidthPx = 4000; o.MaxHeightPx = 4000; });
        var content   = new MemoryStream(ImageTestBytes.BuildPng(5000, 5000));

        var result = validator.ValidateImageContent(content, "image/png");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("exceed the maximum", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateImageContent_Jpeg_BelowMinimumDimensions_ReturnsFailure()
    {
        var validator = CreateValidator(o => { o.MinWidthPx = 100; o.MinHeightPx = 100; });
        var content   = new MemoryStream(ImageTestBytes.BuildJpeg(50, 50));

        var result = validator.ValidateImageContent(content, "image/jpeg");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("smaller than the minimum", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateImageContent_Jpeg_AboveMaximumDimensions_ReturnsFailure()
    {
        var validator = CreateValidator(o => { o.MaxWidthPx = 4000; o.MaxHeightPx = 4000; });
        var content   = new MemoryStream(ImageTestBytes.BuildJpeg(5000, 5000));

        var result = validator.ValidateImageContent(content, "image/jpeg");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Contains("exceed the maximum", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateImageContent_TruncatedPngHeader_ReturnsUnableToDetermineDimensions()
    {
        var content = new MemoryStream(ImageTestBytes.BuildTruncatedPng());

        var result = CreateValidator().ValidateImageContent(content, "image/png");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal("Unable to determine image dimensions.", result.Error.Message);
    }

    [Fact]
    public void ValidateImageContent_TruncatedJpegHeader_ReturnsUnableToDetermineDimensions()
    {
        var content = new MemoryStream(ImageTestBytes.BuildTruncatedJpeg());

        var result = CreateValidator().ValidateImageContent(content, "image/jpeg");

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal("Unable to determine image dimensions.", result.Error.Message);
    }
}
