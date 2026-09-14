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
    string? ReligionOrBeliefSelfDescribed,
    CaringResponsibilities? CaringResponsibilities)
{
    // Populated by the endpoint from the optional "Idempotency-Key" request header (ticket 3, P1
    // follow-up). Null when the caller didn't supply one, in which case no dedup is attempted.
    internal string? IdempotencyKey { get; init; }
}
