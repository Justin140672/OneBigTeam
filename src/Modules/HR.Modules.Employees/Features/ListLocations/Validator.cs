using FluentValidation;

namespace HR.Modules.Employees.Features.ListLocations;

internal sealed class ListLocationsValidator : AbstractValidator<ListLocationsRequest>
{
    public ListLocationsValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();
    }
}
