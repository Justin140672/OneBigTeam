using FluentValidation;
using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.SaveMyEqualityData;

internal sealed class SaveMyEqualityDataValidator : AbstractValidator<SaveMyEqualityDataRequest>
{
    private const int SelfDescribedMaxLength = 250;
    private const int DisabilityImpactMaxLength = 2000;

    public SaveMyEqualityDataValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();
        RuleFor(r => r.EmployeeId).NotEmpty();

        RuleFor(r => r.GenderIdentity).IsInEnum().When(r => r.GenderIdentity.HasValue);
        RuleFor(r => r.MarriedOrCivilPartnershipStatus).IsInEnum().When(r => r.MarriedOrCivilPartnershipStatus.HasValue);
        RuleFor(r => r.EthnicGroup).IsInEnum().When(r => r.EthnicGroup.HasValue);
        RuleFor(r => r.DisabilityStatus).IsInEnum().When(r => r.DisabilityStatus.HasValue);
        RuleFor(r => r.SexualOrientation).IsInEnum().When(r => r.SexualOrientation.HasValue);
        RuleFor(r => r.ReligionOrBelief).IsInEnum().When(r => r.ReligionOrBelief.HasValue);

        RuleFor(r => r.GenderIdentitySelfDescribed).MaximumLength(SelfDescribedMaxLength);
        RuleFor(r => r.EthnicGroupSelfDescribed).MaximumLength(SelfDescribedMaxLength);
        RuleFor(r => r.SexualOrientationSelfDescribed).MaximumLength(SelfDescribedMaxLength);
        RuleFor(r => r.ReligionOrBeliefSelfDescribed).MaximumLength(SelfDescribedMaxLength);
        RuleFor(r => r.DisabilityImpact).MaximumLength(DisabilityImpactMaxLength);

        SelfDescribed(r => r.GenderIdentitySelfDescribed, r => r.GenderIdentity == Domain.GenderIdentity.SelfDescribed, "gender identity");
        SelfDescribed(r => r.EthnicGroupSelfDescribed, r => r.EthnicGroup == Domain.EthnicGroup.SelfDescribed, "ethnic group");
        SelfDescribed(r => r.SexualOrientationSelfDescribed, r => r.SexualOrientation == Domain.SexualOrientation.SelfDescribed, "sexual orientation");
        SelfDescribed(r => r.ReligionOrBeliefSelfDescribed, r => r.ReligionOrBelief == Domain.ReligionOrBelief.SelfDescribed, "religion or belief");
    }

    private void SelfDescribed(
        System.Linq.Expressions.Expression<Func<SaveMyEqualityDataRequest, string?>> selector,
        Func<SaveMyEqualityDataRequest, bool> isSelfDescribed,
        string label)
    {
        RuleFor(selector)
            .Must(v => !string.IsNullOrWhiteSpace(v))
            .When(isSelfDescribed)
            .WithMessage($"A self-described {label} value is required when '{label}' is set to self-described.");

        RuleFor(selector)
            .Must(v => string.IsNullOrWhiteSpace(v))
            .When(r => !isSelfDescribed(r))
            .WithMessage($"A self-described {label} value is only allowed when '{label}' is set to self-described.");
    }
}
