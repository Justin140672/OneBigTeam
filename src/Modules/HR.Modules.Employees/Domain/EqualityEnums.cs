namespace HR.Modules.Employees.Domain;

internal enum GenderIdentity
{
    NotSpecified = 0,
    Man,
    Woman,
    NonBinary,
    SelfDescribed,
    PreferNotToSay
}

internal enum MarriedOrCivilPartnershipStatus
{
    NotSpecified = 0,
    Yes,
    No,
    PreferNotToSay
}

internal enum EthnicGroup
{
    NotSpecified = 0,
    White,
    Mixed,
    AsianOrAsianBritish,
    BlackOrAfricanOrCaribbeanOrBlackBritish,
    OtherEthnicGroup,
    SelfDescribed,
    PreferNotToSay
}

internal enum DisabilityStatus
{
    NotSpecified = 0,
    Yes,
    No,
    PreferNotToSay
}

internal enum CaringResponsibilities
{
    NotSpecified = 0,
    Yes,
    No,
    PreferNotToSay
}

internal enum SexualOrientation
{
    NotSpecified = 0,
    HeterosexualOrStraight,
    GayOrLesbian,
    Bisexual,
    Other,
    SelfDescribed,
    PreferNotToSay
}

internal enum ReligionOrBelief
{
    NotSpecified = 0,
    NoReligion,
    Christian,
    Buddhist,
    Hindu,
    Jewish,
    Muslim,
    Sikh,
    OtherReligion,
    SelfDescribed,
    PreferNotToSay
}
