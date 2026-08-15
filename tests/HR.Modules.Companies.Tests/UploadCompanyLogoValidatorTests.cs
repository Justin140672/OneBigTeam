using HR.Modules.Companies.Domain;
using HR.Modules.Companies.Features.UploadCompanyLogo;

namespace HR.Modules.Companies.Tests;

public class UploadCompanyLogoValidatorTests
{
    [Fact]
    public void Validate_Passes_For_Valid_Png_Request()
    {
        var validator = new UploadCompanyLogoValidator();

        var result = validator.Validate(new UploadCompanyLogoRequest
        {
            Id = Guid.NewGuid(),
            AssetType = BrandingAssetType.PrimaryLogo,
            FileName = "logo.png",
            ContentType = "image/png",
            FileSizeBytes = 512 * 1024,
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Passes_For_Valid_Svg_Request()
    {
        var validator = new UploadCompanyLogoValidator();

        var result = validator.Validate(new UploadCompanyLogoRequest
        {
            Id = Guid.NewGuid(),
            AssetType = BrandingAssetType.SmallLogo,
            FileName = "small.svg",
            ContentType = "image/svg+xml",
            FileSizeBytes = 10 * 1024,
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_FileName_Is_Empty()
    {
        var validator = new UploadCompanyLogoValidator();

        var result = validator.Validate(new UploadCompanyLogoRequest
        {
            Id = Guid.NewGuid(),
            AssetType = BrandingAssetType.PrimaryLogo,
            FileName = string.Empty,
            ContentType = "image/png",
            FileSizeBytes = 1024,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadCompanyLogoRequest.FileName));
    }

    [Fact]
    public void Validate_Fails_For_Disallowed_ContentType()
    {
        var validator = new UploadCompanyLogoValidator();

        var result = validator.Validate(new UploadCompanyLogoRequest
        {
            Id = Guid.NewGuid(),
            AssetType = BrandingAssetType.PrimaryLogo,
            FileName = "logo.gif",
            ContentType = "image/gif",
            FileSizeBytes = 1024,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadCompanyLogoRequest.ContentType));
    }

    [Fact]
    public void Validate_Fails_When_FileSizeBytes_Is_Zero()
    {
        var validator = new UploadCompanyLogoValidator();

        var result = validator.Validate(new UploadCompanyLogoRequest
        {
            Id = Guid.NewGuid(),
            AssetType = BrandingAssetType.PrimaryLogo,
            FileName = "logo.png",
            ContentType = "image/png",
            FileSizeBytes = 0,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadCompanyLogoRequest.FileSizeBytes));
    }

    [Fact]
    public void Validate_Passes_When_FileSizeBytes_Is_Exactly_2MB()
    {
        var validator = new UploadCompanyLogoValidator();

        var result = validator.Validate(new UploadCompanyLogoRequest
        {
            Id = Guid.NewGuid(),
            AssetType = BrandingAssetType.PrimaryLogo,
            FileName = "logo.png",
            ContentType = "image/png",
            FileSizeBytes = 2 * 1024 * 1024,
        });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_Fails_When_File_Exceeds_2MB()
    {
        var validator = new UploadCompanyLogoValidator();

        var result = validator.Validate(new UploadCompanyLogoRequest
        {
            Id = Guid.NewGuid(),
            AssetType = BrandingAssetType.PrimaryLogo,
            FileName = "logo.png",
            ContentType = "image/png",
            FileSizeBytes = 2 * 1024 * 1024 + 1,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadCompanyLogoRequest.FileSizeBytes));
    }

    [Fact]
    public void Validate_Fails_When_AssetType_Is_Not_A_Defined_Enum_Value()
    {
        var validator = new UploadCompanyLogoValidator();

        var result = validator.Validate(new UploadCompanyLogoRequest
        {
            Id = Guid.NewGuid(),
            AssetType = (BrandingAssetType)99,
            FileName = "logo.png",
            ContentType = "image/png",
            FileSizeBytes = 1024,
        });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(UploadCompanyLogoRequest.AssetType));
    }
}
