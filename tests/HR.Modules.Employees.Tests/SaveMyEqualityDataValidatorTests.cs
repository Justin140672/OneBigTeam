using HR.Modules.Employees.Domain;
using HR.Modules.Employees.Features.SaveMyEqualityData;

namespace HR.Modules.Employees.Tests;

public class SaveMyEqualityDataValidatorTests
{
    private static readonly SaveMyEqualityDataValidator Validator = new();

    private static SaveMyEqualityDataRequest Valid() => new(
        CompanyId: Guid.NewGuid(),
        EmployeeId: Guid.NewGuid(),
        GenderIdentity: null,
        GenderIdentitySelfDescribed: null,
        MarriedOrCivilPartnershipStatus: null,
        EthnicGroup: null,
        EthnicGroupSelfDescribed: null,
        DisabilityStatus: null,
        DisabilityImpact: null,
        SexualOrientation: null,
        SexualOrientationSelfDescribed: null,
        ReligionOrBelief: null,
        ReligionOrBeliefSelfDescribed: null);

    // ── Happy paths ────────────────────────────────────────────────────────────

    [Fact]
    public void Passes_When_All_Answers_Null()
        => Assert.True(Validator.Validate(Valid()).IsValid);

    [Fact]
    public void Passes_For_A_Mix_Of_Set_Enums_Without_Free_Text()
    {
        var request = Valid() with
        {
            GenderIdentity = GenderIdentity.Man,
            MarriedOrCivilPartnershipStatus = MarriedOrCivilPartnershipStatus.No,
            EthnicGroup = EthnicGroup.White,
            DisabilityStatus = DisabilityStatus.PreferNotToSay,
            SexualOrientation = SexualOrientation.HeterosexualOrStraight,
            ReligionOrBelief = ReligionOrBelief.NoReligion
        };

        Assert.True(Validator.Validate(request).IsValid);
    }

    [Fact]
    public void Passes_When_SelfDescribed_Enum_Has_Matching_Free_Text()
    {
        var request = Valid() with
        {
            EthnicGroup = EthnicGroup.SelfDescribed,
            EthnicGroupSelfDescribed = "Cornish",
            GenderIdentity = GenderIdentity.SelfDescribed,
            GenderIdentitySelfDescribed = "Agender",
            SexualOrientation = SexualOrientation.SelfDescribed,
            SexualOrientationSelfDescribed = "Queer",
            ReligionOrBelief = ReligionOrBelief.SelfDescribed,
            ReligionOrBeliefSelfDescribed = "Quaker"
        };

        Assert.True(Validator.Validate(request).IsValid);
    }

    [Fact]
    public void Passes_When_DisabilityStatus_Yes_With_Impact_Text()
    {
        var request = Valid() with
        {
            DisabilityStatus = DisabilityStatus.Yes,
            DisabilityImpact = "Reduced mobility on stairs."
        };

        Assert.True(Validator.Validate(request).IsValid);
    }

    // ── Enum range ─────────────────────────────────────────────────────────────

    [Fact]
    public void Fails_When_Enum_Is_Out_Of_Range()
    {
        var request = Valid() with { GenderIdentity = (GenderIdentity)999 };
        Assert.False(Validator.Validate(request).IsValid);
    }

    [Fact]
    public void Fails_When_EthnicGroup_Enum_Is_Out_Of_Range()
    {
        var request = Valid() with { EthnicGroup = (EthnicGroup)(-1) };
        Assert.False(Validator.Validate(request).IsValid);
    }

    // ── Self-described: required-when branch ───────────────────────────────────

    [Fact]
    public void Fails_When_SelfDescribed_Selected_But_Free_Text_Is_Null()
    {
        var request = Valid() with { EthnicGroup = EthnicGroup.SelfDescribed, EthnicGroupSelfDescribed = null };
        Assert.False(Validator.Validate(request).IsValid);
    }

    [Fact]
    public void Fails_When_SelfDescribed_Selected_But_Free_Text_Is_Whitespace()
    {
        var request = Valid() with { EthnicGroup = EthnicGroup.SelfDescribed, EthnicGroupSelfDescribed = "   " };
        Assert.False(Validator.Validate(request).IsValid);
    }

    [Fact]
    public void Fails_When_SelfDescribed_Selected_But_Free_Text_Is_Empty()
    {
        var request = Valid() with { SexualOrientation = SexualOrientation.SelfDescribed, SexualOrientationSelfDescribed = string.Empty };
        Assert.False(Validator.Validate(request).IsValid);
    }

    // ── Self-described: not-allowed-otherwise branch ──────────────────────────

    [Fact]
    public void Fails_When_Free_Text_Provided_But_Paired_Enum_Is_A_Non_SelfDescribed_Value()
    {
        var request = Valid() with { EthnicGroup = EthnicGroup.White, EthnicGroupSelfDescribed = "Cornish" };
        Assert.False(Validator.Validate(request).IsValid);
    }

    [Fact]
    public void Fails_When_Free_Text_Provided_But_Paired_Enum_Is_Null()
    {
        var request = Valid() with { EthnicGroup = null, EthnicGroupSelfDescribed = "Cornish" };
        Assert.False(Validator.Validate(request).IsValid);
    }

    [Fact]
    public void Fails_When_Gender_Free_Text_Provided_But_Gender_Enum_Is_Man()
    {
        var request = Valid() with { GenderIdentity = GenderIdentity.Man, GenderIdentitySelfDescribed = "Something" };
        Assert.False(Validator.Validate(request).IsValid);
    }

    // ── Length boundaries ─────────────────────────────────────────────────────

    [Fact]
    public void Passes_When_SelfDescribed_Is_Exactly_250_Characters()
    {
        var request = Valid() with
        {
            ReligionOrBelief = ReligionOrBelief.SelfDescribed,
            ReligionOrBeliefSelfDescribed = new string('a', 250)
        };

        Assert.True(Validator.Validate(request).IsValid);
    }

    [Fact]
    public void Fails_When_SelfDescribed_Exceeds_250_Characters()
    {
        var request = Valid() with
        {
            ReligionOrBelief = ReligionOrBelief.SelfDescribed,
            ReligionOrBeliefSelfDescribed = new string('a', 251)
        };

        Assert.False(Validator.Validate(request).IsValid);
    }

    [Fact]
    public void Passes_When_DisabilityImpact_Is_Exactly_2000_Characters()
    {
        var request = Valid() with { DisabilityStatus = DisabilityStatus.Yes, DisabilityImpact = new string('a', 2000) };
        Assert.True(Validator.Validate(request).IsValid);
    }

    [Fact]
    public void Fails_When_DisabilityImpact_Exceeds_2000_Characters()
    {
        var request = Valid() with { DisabilityStatus = DisabilityStatus.Yes, DisabilityImpact = new string('a', 2001) };
        Assert.False(Validator.Validate(request).IsValid);
    }

    // ── Route ids ─────────────────────────────────────────────────────────────

    [Fact]
    public void Fails_When_CompanyId_Is_Empty()
        => Assert.False(Validator.Validate(Valid() with { CompanyId = Guid.Empty }).IsValid);

    [Fact]
    public void Fails_When_EmployeeId_Is_Empty()
        => Assert.False(Validator.Validate(Valid() with { EmployeeId = Guid.Empty }).IsValid);
}
