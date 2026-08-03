using FluentValidation;

namespace HR.Modules.Employees.Features.GetGenderSplit;

internal sealed class GetGenderSplitValidator : AbstractValidator<GetGenderSplitRequest>
{
    public GetGenderSplitValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
