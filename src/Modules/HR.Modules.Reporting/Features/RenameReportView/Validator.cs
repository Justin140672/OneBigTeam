using FluentValidation;

namespace HR.Modules.Reporting.Features.RenameReportView;

internal sealed class RenameReportViewValidator : AbstractValidator<RenameReportViewRequest>
{
    public RenameReportViewValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.ViewId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}
