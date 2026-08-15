using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class CompanySettingsEmployeeNumberTests
{
    [Fact]
    public void CreateDefault_Sets_Automatic_Mode_And_Default_Numbering_Values()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), now);

        Assert.Equal(EmployeeNumberMode.Automatic, settings.EmployeeNumberMode);
        Assert.Null(settings.EmployeeNumberPrefix);
        Assert.Equal(1, settings.NextEmployeeNumber);
        Assert.Equal(4, settings.EmployeeNumberMinimumLength);
    }

    [Fact]
    public void Update_Sets_EmployeeNumber_Fields()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), now);

        settings.UpdateCompanyProfile("UTC", "en-GB", now);
        settings.UpdateHrPolicy(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
                             WorkingDays.Thursday | WorkingDays.Friday,
            7.5m, 1, 25, 6, true, false, false, null, null,
            "Custom statement.", 3, NoticePeriodUnit.Months, 1, true,
            EmployeeNumberMode.Automatic, "EMP-", 125, 5, now);

        Assert.Equal(EmployeeNumberMode.Automatic, settings.EmployeeNumberMode);
        Assert.Equal("EMP-", settings.EmployeeNumberPrefix);
        Assert.Equal(125, settings.NextEmployeeNumber);
        Assert.Equal(5, settings.EmployeeNumberMinimumLength);
    }

    [Fact]
    public void Update_Normalises_Blank_EmployeeNumberPrefix_To_Null()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), now);

        settings.UpdateCompanyProfile("UTC", "en-GB", now);
        settings.UpdateHrPolicy(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
                             WorkingDays.Thursday | WorkingDays.Friday,
            7.5m, 1, 25, 6, true, false, false, null, null,
            "Custom statement.", 3, NoticePeriodUnit.Months, 1, true,
            EmployeeNumberMode.Manual, "   ", 1, 1, now);

        Assert.Null(settings.EmployeeNumberPrefix);
    }

    [Fact]
    public void Update_Trims_EmployeeNumberPrefix()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), now);

        settings.UpdateCompanyProfile("UTC", "en-GB", now);
        settings.UpdateHrPolicy(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
                             WorkingDays.Thursday | WorkingDays.Friday,
            7.5m, 1, 25, 6, true, false, false, null, null,
            "Custom statement.", 3, NoticePeriodUnit.Months, 1, true,
            EmployeeNumberMode.Manual, "  EMP-  ", 1, 1, now);

        Assert.Equal("EMP-", settings.EmployeeNumberPrefix);
    }

    [Fact]
    public void Update_Preserves_Null_EmployeeNumberPrefix()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), now);

        settings.UpdateCompanyProfile("UTC", "en-GB", now);
        settings.UpdateHrPolicy(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
                             WorkingDays.Thursday | WorkingDays.Friday,
            7.5m, 1, 25, 6, true, false, false, null, null,
            "Custom statement.", 3, NoticePeriodUnit.Months, 1, true,
            EmployeeNumberMode.Automatic, null, 1, 1, now);

        Assert.Null(settings.EmployeeNumberPrefix);
    }

    [Fact]
    public void UpdateHrPolicy_Falls_Back_To_Default_Text_When_AcknowledgementStatement_Is_Empty()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), now);

        settings.UpdateCompanyProfile("UTC", "en-GB", now);
        settings.UpdateHrPolicy(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
                             WorkingDays.Thursday | WorkingDays.Friday,
            7.5m, 1, 25, 6, true, false, false, null, null,
            string.Empty, 3, NoticePeriodUnit.Months, 1, true,
            EmployeeNumberMode.Automatic, null, 1, 1, now);

        Assert.Equal(CompanySettings.DefaultAcknowledgementStatementText, settings.DefaultAcknowledgementStatement);
    }

    [Fact]
    public void UpdateHrPolicy_Falls_Back_To_Default_Text_When_AcknowledgementStatement_Is_Whitespace()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), now);

        settings.UpdateCompanyProfile("UTC", "en-GB", now);
        settings.UpdateHrPolicy(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
                             WorkingDays.Thursday | WorkingDays.Friday,
            7.5m, 1, 25, 6, true, false, false, null, null,
            "   ", 3, NoticePeriodUnit.Months, 1, true,
            EmployeeNumberMode.Automatic, null, 1, 1, now);

        Assert.Equal(CompanySettings.DefaultAcknowledgementStatementText, settings.DefaultAcknowledgementStatement);
    }

    [Fact]
    public void UpdateHrPolicy_Preserves_Custom_AcknowledgementStatement_When_Not_Blank()
    {
        var now = DateTimeOffset.UtcNow;
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), now);

        settings.UpdateCompanyProfile("UTC", "en-GB", now);
        settings.UpdateHrPolicy(
            WorkingDays.Monday | WorkingDays.Tuesday | WorkingDays.Wednesday |
                             WorkingDays.Thursday | WorkingDays.Friday,
            7.5m, 1, 25, 6, true, false, false, null, null,
            "Custom statement.", 3, NoticePeriodUnit.Months, 1, true,
            EmployeeNumberMode.Automatic, null, 1, 1, now);

        Assert.Equal("Custom statement.", settings.DefaultAcknowledgementStatement);
    }
}
