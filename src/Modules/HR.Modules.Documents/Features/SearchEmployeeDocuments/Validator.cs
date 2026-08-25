using FluentValidation;

namespace HR.Modules.Documents.Features.SearchEmployeeDocuments;

internal sealed class SearchEmployeeDocumentsValidator : AbstractValidator<SearchEmployeeDocumentsRequest>
{
    public SearchEmployeeDocumentsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();

        RuleFor(r => r.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);

        RuleFor(r => r.SearchText).MaximumLength(200);

        RuleFor(r => r.UploadedTo)
            .GreaterThanOrEqualTo(r => r.UploadedFrom!.Value)
            .When(r => r.UploadedFrom is not null && r.UploadedTo is not null)
            .WithMessage("UploadedTo must not be earlier than UploadedFrom.");

        RuleFor(r => r.ExpiresTo)
            .GreaterThanOrEqualTo(r => r.ExpiresFrom!.Value)
            .When(r => r.ExpiresFrom is not null && r.ExpiresTo is not null)
            .WithMessage("ExpiresTo must not be earlier than ExpiresFrom.");
    }
}
