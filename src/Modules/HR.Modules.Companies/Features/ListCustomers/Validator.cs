using FluentValidation;

namespace HR.Modules.Companies.Features.ListCustomers;

internal sealed class ListCustomersValidator : AbstractValidator<ListCustomersRequest>
{
    public ListCustomersValidator()
    {
        RuleFor(r => r.Search)
            .MaximumLength(200)
            .When(r => r.Search is not null);
    }
}
