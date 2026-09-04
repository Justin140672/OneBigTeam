using HR.Modules.Employees.Domain;

namespace HR.Modules.Employees.Features.GetMyEqualityData;

internal static class EqualityDataResponseMapper
{
    public static GetMyEqualityDataResponse Empty()
        => new(false, null, null, null, null, null, null, null, null, null, null, null, null, null);

    public static GetMyEqualityDataResponse FromEntity(EmployeeEqualityData record)
        => new(
            true,
            EqualityEnumMapping.FromStored<GenderIdentity>(record.GenderIdentity),
            record.GenderIdentitySelfDescribed,
            EqualityEnumMapping.FromStored<MarriedOrCivilPartnershipStatus>(record.MarriedOrCivilPartnershipStatus),
            EqualityEnumMapping.FromStored<EthnicGroup>(record.EthnicGroup),
            record.EthnicGroupSelfDescribed,
            EqualityEnumMapping.FromStored<DisabilityStatus>(record.DisabilityStatus),
            record.DisabilityImpact,
            EqualityEnumMapping.FromStored<SexualOrientation>(record.SexualOrientation),
            record.SexualOrientationSelfDescribed,
            EqualityEnumMapping.FromStored<ReligionOrBelief>(record.ReligionOrBelief),
            record.ReligionOrBeliefSelfDescribed,
            record.CreatedAt,
            record.UpdatedAt);
}
