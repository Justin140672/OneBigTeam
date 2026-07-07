using FluentValidation;

namespace HR.Modules.Employees.Features.GetLocation;

internal sealed class GetLocationValidator : AbstractValidator<GetLocationRequest>
{
    public GetLocationValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.Id).NotEmpty();
    }
}
