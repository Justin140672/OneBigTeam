using HR.Modules.Companies.Domain;

namespace HR.Modules.Companies.Tests;

public class CompanySettingsProbationCheckpointsTests
{
    [Fact]
    public void CreateDefault_Sets_Default_Checkpoint_Days_To_30_60_90()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(30, settings.ProbationCheckpointDay1);
        Assert.Equal(60, settings.ProbationCheckpointDay2);
        Assert.Equal(90, settings.ProbationCheckpointDay3);
    }

    [Fact]
    public void UpdateProbationCheckpoints_Sets_New_Checkpoint_Days()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateProbationCheckpoints(14, 45, null, DateTimeOffset.UtcNow);

        Assert.Equal(14, settings.ProbationCheckpointDay1);
        Assert.Equal(45, settings.ProbationCheckpointDay2);
        Assert.Null(settings.ProbationCheckpointDay3);
    }

    [Fact]
    public void UpdateProbationCheckpoints_Allows_Disabling_All_Checkpoints()
    {
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), DateTimeOffset.UtcNow);

        settings.UpdateProbationCheckpoints(null, null, null, DateTimeOffset.UtcNow);

        Assert.Null(settings.ProbationCheckpointDay1);
        Assert.Null(settings.ProbationCheckpointDay2);
        Assert.Null(settings.ProbationCheckpointDay3);
    }

    [Fact]
    public void UpdateProbationCheckpoints_Updates_UpdatedAt()
    {
        var createdAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var settings = CompanySettings.CreateDefault(Guid.NewGuid(), createdAt);
        var updatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        settings.UpdateProbationCheckpoints(20, 50, 80, updatedAt);

        Assert.Equal(updatedAt, settings.UpdatedAt);
    }
}
