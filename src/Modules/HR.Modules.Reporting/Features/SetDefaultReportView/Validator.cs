using FluentValidation;

namespace HR.Modules.Reporting.Features.SetDefaultReportView;

internal sealed class SetDefaultReportViewValidator : AbstractValidator<SetDefaultReportViewRequest>
{
    public SetDefaultReportViewValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.ViewId).NotEmpty();
    }
}
