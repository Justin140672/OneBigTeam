using FluentValidation;

namespace HR.Modules.Companies.Features.GetHrSettingsHistory;

internal sealed class GetHrSettingsHistoryValidator : AbstractValidator<GetHrSettingsHistoryRequest>
{
    public GetHrSettingsHistoryValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
    }
}
