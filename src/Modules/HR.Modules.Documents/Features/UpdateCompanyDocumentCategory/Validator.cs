using FluentValidation;

namespace HR.Modules.Documents.Features.UpdateCompanyDocumentCategory;

internal sealed class UpdateCompanyDocumentCategoryValidator : AbstractValidator<UpdateCompanyDocumentCategoryRequest>
{
    public UpdateCompanyDocumentCategoryValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.CategoryId)
            .NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(100);
    }
}
