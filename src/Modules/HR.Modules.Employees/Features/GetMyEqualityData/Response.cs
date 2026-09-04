using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.GetMyEqualityData;

internal sealed record GetMyEqualityDataResponse(
    bool HasRecord,
    GenderIdentity? GenderIdentity,
    string? GenderIdentitySelfDescribed,
    MarriedOrCivilPartnershipStatus? MarriedOrCivilPartnershipStatus,
    EthnicGroup? EthnicGroup,
    string? EthnicGroupSelfDescribed,
    DisabilityStatus? DisabilityStatus,
    string? DisabilityImpact,
    SexualOrientation? SexualOrientation,
    string? SexualOrientationSelfDescribed,
    ReligionOrBelief? ReligionOrBelief,
    string? ReligionOrBeliefSelfDescribed,
    CaringResponsibilities? CaringResponsibilities,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);
