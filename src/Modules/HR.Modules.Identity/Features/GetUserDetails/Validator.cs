using FluentValidation;

namespace HR.Modules.Identity.Features.GetUserDetails;

internal sealed class GetUserDetailsValidator : AbstractValidator<GetUserDetailsRequest>
{
    public GetUserDetailsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();
    }
}
