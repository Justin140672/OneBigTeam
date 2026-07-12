using FluentValidation;

namespace HR.Modules.Documents.Features.UploadEmployeeProfilePhoto;

internal sealed class UploadEmployeeProfilePhotoValidator : AbstractValidator<UploadEmployeeProfilePhotoRequest>
{
    public UploadEmployeeProfilePhotoValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();

        RuleFor(r => r.File)
            .NotNull()
            .WithMessage("A file must be provided.");
    }
}
