using FluentValidation;

namespace HR.Modules.Documents.Features.GetExpiringDocuments;

internal sealed class GetExpiringDocumentsValidator : AbstractValidator<GetExpiringDocumentsRequest>
{
    public GetExpiringDocumentsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
    }
}
