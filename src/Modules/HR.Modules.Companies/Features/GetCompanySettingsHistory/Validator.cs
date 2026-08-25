using FluentValidation;

namespace HR.Modules.Companies.Features.GetCompanySettingsHistory;

internal sealed class GetCompanySettingsHistoryValidator : AbstractValidator<GetCompanySettingsHistoryRequest>
{
    public GetCompanySettingsHistoryValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
    }
}
