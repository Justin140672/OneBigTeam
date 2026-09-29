using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

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
        Assert.Equal(1, CompanySicknessSettings.Default.ReturnToWorkRequiredAfterDays);
        Assert.Equal(1, CompanySettings.CreateDefault(Guid.NewGuid(), Now).ReturnToWorkRequiredAfterDays);
    }
}
