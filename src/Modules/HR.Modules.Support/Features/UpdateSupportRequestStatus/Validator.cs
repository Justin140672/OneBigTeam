using FluentValidation;

namespace HR.Modules.Support.Features.UpdateSupportRequestStatus;

internal sealed class UpdateSupportRequestStatusValidator : AbstractValidator<UpdateSupportRequestStatusRequest>
{
    public UpdateSupportRequestStatusValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Status).IsInEnum();
    }
}
