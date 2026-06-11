using FluentValidation;

namespace HR.Modules.Employees.Features.ListPositionProfiles;

internal sealed class ListPositionProfilesValidator : AbstractValidator<ListPositionProfilesRequest>
{
    public ListPositionProfilesValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();
    }
}
