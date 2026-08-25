using HR.Modules.Companies.Contracts;
using HR.Modules.Companies.Domain;
using HR.Modules.Employees.Contracts;

namespace HR.Modules.Companies.Tests.Domain;

/// <summary>
/// SET-03: CompanySettings.Version is a persisted, application-managed optimistic-concurrency
/// token, incremented by exactly one on every mutation method regardless of which slice of fields
/// it touches (company profile vs HR policy vs asset numbering vs probation checkpoints).
/// </summary>
public class CompanySettingsVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateDefault_Starts_Version_At_One()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), Now);

        Assert.Equal(1, settings.Version);
    }

    [Fact]
    public void UpdateCompanyProfile_Increments_Version_By_One()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), Now);

        settings.UpdateCompanyProfile("UTC", "en-GB", Now);

        Assert.Equal(2, settings.Version);
    }

    [Fact]
    public void UpdateHrPolicy_Increments_Version_By_One()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), Now);

        settings.UpdateHrPolicy(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday | WorkingDays.Thursday | WorkingDays.Friday,
            7.5m,
            1,
            25,
            6,
            excludePublicHolidaysFromLeave: true,
            excludePublicHolidaysFromSickness: false,
            displaySalaryOnEmployeeProfile: false,
            fitNoteRequiredAfterDays: 7,
            returnToWorkRequiredAfterDays: 1,
            defaultAcknowledgementStatement: "Statement",
            acknowledgementReminderIntervalDays: 3,
            noticePeriodUnit: NoticePeriodUnit.Months,
            noticePeriodLength: 1,
            autoDisableAccessOnLeavingDate: true,
            employeeNumberMode: EmployeeNumberMode.Automatic,
            employeeNumberPrefix: null,
            nextEmployeeNumber: 1,
            employeeNumberMinimumLength: 4,
            now: Now);

        Assert.Equal(2, settings.Version);
    }

    [Fact]
    public void UpdateProbationCheckpoints_Increments_Version_By_One()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), Now);

        settings.UpdateProbationCheckpoints(30, 60, 90, Now);

        Assert.Equal(2, settings.Version);
    }

    [Fact]
    public void UpdateAttendanceAlertThresholds_Increments_Version_By_One()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), Now);

        settings.UpdateAttendanceAlertThresholds(6, 180, 21, 2, 200, Now);

        Assert.Equal(2, settings.Version);
    }

    [Fact]
    public void UpdateAssetNumberSettings_Increments_Version_By_One()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), Now);

        settings.UpdateAssetNumberSettings(AssetNumberMode.Automatic, "AST-", 1, 4, Now);

        Assert.Equal(2, settings.Version);
    }

    [Fact]
    public void Multiple_Mutations_Increment_Version_Cumulatively()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), Now);

        settings.UpdateCompanyProfile("UTC", "en-GB", Now);
        settings.UpdateProbationCheckpoints(30, 60, 90, Now);
        settings.UpdateAssetNumberSettings(AssetNumberMode.Manual, null, 1, 4, Now);

        Assert.Equal(4, settings.Version);
    }
}
