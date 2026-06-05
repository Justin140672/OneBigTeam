using FluentValidation;

namespace HR.Modules.Companies.Features.UploadCompanyLogo;

internal sealed class UploadCompanyLogoValidator : AbstractValidator<UploadCompanyLogoRequest>
{
    private static readonly string[] AllowedContentTypes = ["image/png", "image/svg+xml"];
    private const long MaxFileSizeBytes = 2 * 1024 * 1024;

    public UploadCompanyLogoValidator()
    {
        RuleFor(r => r.AssetType)
            .IsInEnum()
            .WithMessage("Asset type must be a valid branding asset type.");

        RuleFor(r => r.FileName)
            .NotEmpty()
            .WithMessage("File name is required.");

        RuleFor(r => r.ContentType)
            .NotEmpty()
            .WithMessage("Content type is required.")
            .Must(ct => AllowedContentTypes.Contains(ct))
            .WithMessage($"Content type must be one of: {string.Join(", ", AllowedContentTypes)}.");

        RuleFor(r => r.FileSizeBytes)
            .GreaterThan(0)
            .WithMessage("File size must be greater than zero.")
            .LessThanOrEqualTo(MaxFileSizeBytes)
            .WithMessage("File size must not exceed 2 MB.");
    }
}
