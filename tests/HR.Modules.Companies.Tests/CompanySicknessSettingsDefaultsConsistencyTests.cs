using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

/// <summary>
/// SICK-05 drift-prevention test: the persisted per-company defaults set by
/// CompanySettings.CreateDefault (used when a new company is provisioned) and the
/// CompanySicknessSettings.Default fallback (used by CompanySicknessSettingsReader when a
/// company has no CompanySettings row yet) must always agree. If one changes without the other,
/// new companies and the "no settings row yet" fallback path would silently disagree on
/// return-to-work / fit-note thresholds. See
/// specifications/product-specifications/00-current-product-decisions.md ("Sickness management").
/// </summary>
public class CompanySicknessSettingsDefaultsConsistencyTests
{
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void CreateDefault_ReturnToWorkRequiredAfterDays_Matches_ContractDefault()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), Now);

        Assert.Equal(
            CompanySicknessSettings.Default.ReturnToWorkRequiredAfterDays,
            settings.ReturnToWorkRequiredAfterDays);
    }

    [Fact]
    public void CreateDefault_FitNoteRequiredAfterDays_Matches_ContractDefault()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), Now);

        Assert.Equal(
            CompanySicknessSettings.Default.FitNoteRequiredAfterDays,
            settings.FitNoteRequiredAfterDays);
    }

    [Fact]
    public void ReturnToWorkRequiredAfterDays_Is_Confirmed_As_One_Working_Day()
    {
        // SICK-05: confirmed decision — 1 working day, not the 3 working days an earlier draft of
        // the sickness spec described. See 00-current-product-decisions.md for the rationale.
        Assert.Equal(1, CompanySicknessSettings.Default.ReturnToWorkRequiredAfterDays);
        Assert.Equal(1, CompanySettings.CreateDefault(Guid.NewGuid(), Now).ReturnToWorkRequiredAfterDays);
    }
}
