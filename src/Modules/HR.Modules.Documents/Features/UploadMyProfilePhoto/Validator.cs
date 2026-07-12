using FluentValidation;

namespace HR.Modules.Documents.Features.UploadMyProfilePhoto;

internal sealed class UploadMyProfilePhotoValidator : AbstractValidator<UploadMyProfilePhotoRequest>
{
    public UploadMyProfilePhotoValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();

        RuleFor(r => r.File)
            .NotNull()
            .WithMessage("A file must be provided.");
    }
}
