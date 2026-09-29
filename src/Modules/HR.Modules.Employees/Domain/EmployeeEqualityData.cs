namespace HR.Modules.Employees.Domain;

internal sealed class EmployeeEqualityData
{
    private EmployeeEqualityData() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }

    public string? GenderIdentity { get; private set; }
    public string? GenderIdentitySelfDescribed { get; private set; }
    public string? MarriedOrCivilPartnershipStatus { get; private set; }
    public string? EthnicGroup { get; private set; }
    public string? EthnicGroupSelfDescribed { get; private set; }
    public string? DisabilityStatus { get; private set; }
    public string? DisabilityImpact { get; private set; }
    public string? SexualOrientation { get; private set; }
    public string? SexualOrientationSelfDescribed { get; private set; }
    public string? ReligionOrBelief { get; private set; }
    public string? ReligionOrBeliefSelfDescribed { get; private set; }
    public string? CaringResponsibilities { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static EmployeeEqualityData Create(
        Guid id,
        Guid companyId,
        Guid employeeId,
        string? genderIdentity,
        string? genderIdentitySelfDescribed,
        string? marriedOrCivilPartnershipStatus,
        string? ethnicGroup,
        string? ethnicGroupSelfDescribed,
        string? disabilityStatus,
        string? disabilityImpact,
        string? sexualOrientation,
        string? sexualOrientationSelfDescribed,
        string? religionOrBelief,
        string? religionOrBeliefSelfDescribed,
        string? caringResponsibilities,
        DateTimeOffset now)
    {
        return new EmployeeEqualityData
        {
            Id = id,
            CompanyId = companyId,
            EmployeeId = employeeId,
            GenderIdentity = genderIdentity,
            GenderIdentitySelfDescribed = genderIdentitySelfDescribed,
            MarriedOrCivilPartnershipStatus = marriedOrCivilPartnershipStatus,
            EthnicGroup = ethnicGroup,
            EthnicGroupSelfDescribed = ethnicGroupSelfDescribed,
            DisabilityStatus = disabilityStatus,
            DisabilityImpact = disabilityImpact,
            SexualOrientation = sexualOrientation,
            SexualOrientationSelfDescribed = sexualOrientationSelfDescribed,
            ReligionOrBelief = religionOrBelief,
            ReligionOrBeliefSelfDescribed = religionOrBeliefSelfDescribed,
            CaringResponsibilities = caringResponsibilities,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public void Update(
        string? genderIdentity,
        string? genderIdentitySelfDescribed,
        string? marriedOrCivilPartnershipStatus,
        string? ethnicGroup,
        string? ethnicGroupSelfDescribed,
        string? disabilityStatus,
        string? disabilityImpact,
        string? sexualOrientation,
        string? sexualOrientationSelfDescribed,
        string? religionOrBelief,
        string? religionOrBeliefSelfDescribed,
        string? caringResponsibilities,
        DateTimeOffset now)
    {
        GenderIdentity = genderIdentity;
        GenderIdentitySelfDescribed = genderIdentitySelfDescribed;
        MarriedOrCivilPartnershipStatus = marriedOrCivilPartnershipStatus;
        EthnicGroup = ethnicGroup;
        EthnicGroupSelfDescribed = ethnicGroupSelfDescribed;
        DisabilityStatus = disabilityStatus;
        DisabilityImpact = disabilityImpact;
        SexualOrientation = sexualOrientation;
        SexualOrientationSelfDescribed = sexualOrientationSelfDescribed;
        ReligionOrBelief = religionOrBelief;
        ReligionOrBeliefSelfDescribed = religionOrBeliefSelfDescribed;
        CaringResponsibilities = caringResponsibilities;
        UpdatedAt = now;
    }
}
