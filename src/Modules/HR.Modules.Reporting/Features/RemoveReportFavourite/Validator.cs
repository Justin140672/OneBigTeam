using FluentValidation;

namespace HR.Modules.Reporting.Features.RemoveReportFavourite;

internal sealed class RemoveReportFavouriteValidator : AbstractValidator<RemoveReportFavouriteRequest>
{
    public RemoveReportFavouriteValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.ReportId).NotEmpty().MaximumLength(200);
    }
}
