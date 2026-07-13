using FluentValidation;

namespace HR.Modules.Documents.Features.GetPendingProfilePhotoById;

internal sealed class GetPendingProfilePhotoByIdValidator : AbstractValidator<GetPendingProfilePhotoByIdRequest>
{
    public GetPendingProfilePhotoByIdValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.PendingPhotoId).NotEmpty();
    }
}
