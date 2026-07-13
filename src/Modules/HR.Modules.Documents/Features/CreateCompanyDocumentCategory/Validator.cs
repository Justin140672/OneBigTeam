using FluentValidation;

namespace HR.Modules.Documents.Features.CreateCompanyDocumentCategory;

internal sealed class CreateCompanyDocumentCategoryValidator : AbstractValidator<CreateCompanyDocumentCategoryRequest>
{
    public CreateCompanyDocumentCategoryValidator()
    {
        RuleFor(r => r.CompanyId)
            .NotEmpty();

        RuleFor(r => r.Name)
            .NotEmpty()
            .MaximumLength(100);
    }
}
