using FluentValidation;

namespace HR.Modules.Reporting.Features.DeleteReportView;

internal sealed class DeleteReportViewValidator : AbstractValidator<DeleteReportViewRequest>
{
    public DeleteReportViewValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.ViewId).NotEmpty();
    }
}
