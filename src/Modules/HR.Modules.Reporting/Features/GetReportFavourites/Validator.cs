using FluentValidation;

namespace HR.Modules.Reporting.Features.GetReportFavourites;

internal sealed class GetReportFavouritesValidator : AbstractValidator<GetReportFavouritesRequest>
{
    public GetReportFavouritesValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
    }
}
