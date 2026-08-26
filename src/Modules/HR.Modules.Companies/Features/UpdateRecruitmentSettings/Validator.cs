using FluentValidation;

namespace HR.Modules.Companies.Features.UpdateRecruitmentSettings;

internal sealed class UpdateRecruitmentSettingsValidator : AbstractValidator<UpdateRecruitmentSettingsRequest>
{
    // SET-05: safe minimum/maximum for candidate data retention — at least 90 days (roughly one
    // recruitment cycle) and at most 3650 days (10 years), matching the check constraint on the
    // candidate_retention_days column.
    private const int MinCandidateRetentionDays = 90;
    private const int MaxCandidateRetentionDays = 3650;

    public UpdateRecruitmentSettingsValidator()
    {
        RuleFor(r => r.CompanyId).NotEmpty();

        RuleFor(r => r.CandidateRetentionDays)
            .InclusiveBetween(MinCandidateRetentionDays, MaxCandidateRetentionDays)
            .WithMessage($"Candidate retention days must be between {MinCandidateRetentionDays} and {MaxCandidateRetentionDays}.");

        RuleFor(r => r.Version).GreaterThan(0);
    }
}
