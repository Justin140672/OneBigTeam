using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.SaveMyEqualityData;

internal sealed record SaveMyEqualityDataRequest(
    Guid CompanyId,
    Guid EmployeeId,
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
    string? ReligionOrBeliefSelfDescribed);
