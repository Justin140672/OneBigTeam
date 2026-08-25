using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class CompanySettingsAttendanceAlertThresholdsTests
{
    [Fact]
    public void CreateDefault_Sets_Default_AttendanceAlertThresholds()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(4, settings.FrequentAbsenceCountThreshold);
        Assert.Equal(365, settings.FrequentAbsenceWindowDays);
        Assert.Equal(28, settings.LongAbsenceDayThreshold);
        Assert.Equal(3, settings.WeekdayPatternOccurrenceThreshold);
        Assert.Equal(365, settings.WeekdayPatternWindowDays);
    }

    [Fact]
    public void UpdateAttendanceAlertThresholds_Sets_New_Threshold_Values()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateAttendanceAlertThresholds(6, 180, 21, 2, 200, DateTimeOffset.UtcNow);

        Assert.Equal(6, settings.FrequentAbsenceCountThreshold);
        Assert.Equal(180, settings.FrequentAbsenceWindowDays);
        Assert.Equal(21, settings.LongAbsenceDayThreshold);
        Assert.Equal(2, settings.WeekdayPatternOccurrenceThreshold);
        Assert.Equal(200, settings.WeekdayPatternWindowDays);
    }

    [Fact]
    public void UpdateAttendanceAlertThresholds_Updates_UpdatedAt()
    {
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), createdAt);
        var updatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        settings.UpdateAttendanceAlertThresholds(6, 180, 21, 2, 200, updatedAt);

        Assert.Equal(updatedAt, settings.UpdatedAt);
    }
}
