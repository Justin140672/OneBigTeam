using FluentValidation;

namespace HR.Modules.Documents.Features.ListCompanyDocumentCategories;

internal sealed class ListCompanyDocumentCategoriesValidator : AbstractValidator<ListCompanyDocumentCategoriesRequest>
{
    public ListCompanyDocumentCategoriesValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();
    }
}
