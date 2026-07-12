using FluentValidation;

namespace HR.Modules.Documents.Features.CancelPendingProfilePhoto;

internal sealed class CancelPendingProfilePhotoValidator : AbstractValidator<CancelPendingProfilePhotoRequest>
{
    public CancelPendingProfilePhotoValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
