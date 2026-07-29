using FluentValidation;

namespace HR.Modules.Reporting.Features.AddReportFavourite;

internal sealed class AddReportFavouriteValidator : AbstractValidator<AddReportFavouriteRequest>
{
    public AddReportFavouriteValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.ReportId).NotEmpty().MaximumLength(200);
    }
}
