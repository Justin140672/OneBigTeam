using FluentValidation;

namespace HR.Modules.Documents.Features.GetPendingProfilePhoto;

internal sealed class GetPendingProfilePhotoValidator : AbstractValidator<GetPendingProfilePhotoRequest>
{
    public GetPendingProfilePhotoValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
    }
}
