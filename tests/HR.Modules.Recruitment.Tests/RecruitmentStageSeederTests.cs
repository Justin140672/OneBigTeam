using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using HR.Modules.Recruitment.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Recruitment.Tests;

public class RecruitmentStageSeederTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnsureDefaultStagesSeededAsync_Seeds_Six_Default_Stages_For_New_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        await new RecruitmentStageSeeder(db).EnsureDefaultStagesSeededAsync(companyId, Now, CancellationToken.None);

        var stages = await db.RecruitmentStages.Where(s => s.CompanyId == companyId).OrderBy(s => s.DisplayOrder).ToListAsync();
        Assert.Equal(6, stages.Count);
        Assert.Equal(
            ["Application Received", "CV Review", "Interview", "Offer", "Hired", "Rejected"],
            stages.Select(s => s.Name));
        Assert.Equal([1, 2, 3, 4, 5, 6], stages.Select(s => s.DisplayOrder));
        Assert.All(stages, s => Assert.True(s.IsActive));

        var hired = stages.Single(s => s.Name == "Hired");
        Assert.True(hired.IsTerminal);
        Assert.Equal(RecruitmentStageTerminalOutcome.Hired, hired.TerminalOutcome);

        var rejected = stages.Single(s => s.Name == "Rejected");
        Assert.True(rejected.IsTerminal);
        Assert.Equal(RecruitmentStageTerminalOutcome.Rejected, rejected.TerminalOutcome);

        Assert.All(stages.Where(s => s.Name is not ("Hired" or "Rejected")), s => Assert.False(s.IsTerminal));
    }

    [Fact]
    public async Task EnsureDefaultStagesSeededAsync_Assigns_Expected_Stage_Purposes()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        await new RecruitmentStageSeeder(db).EnsureDefaultStagesSeededAsync(companyId, Now, CancellationToken.None);

        var stages = await db.RecruitmentStages.Where(s => s.CompanyId == companyId).ToListAsync();
        Assert.Equal(RecruitmentStagePurpose.NewApplication, stages.Single(s => s.Name == "Application Received").Purpose);
        Assert.Equal(RecruitmentStagePurpose.Interview, stages.Single(s => s.Name == "Interview").Purpose);
        Assert.Equal(RecruitmentStagePurpose.Offer, stages.Single(s => s.Name == "Offer").Purpose);
        Assert.Null(stages.Single(s => s.Name == "CV Review").Purpose);
        Assert.Null(stages.Single(s => s.Name == "Hired").Purpose);
        Assert.Null(stages.Single(s => s.Name == "Rejected").Purpose);
    }

    [Fact]
    public void BuildDefaultStages_Assigns_Expected_Stage_Purposes()
    {
        var stages = RecruitmentStageSeeder.BuildDefaultStages(Guid.NewGuid(), Now);

        Assert.Equal(RecruitmentStagePurpose.NewApplication, stages.Single(s => s.Name == "Application Received").Purpose);
        Assert.Equal(RecruitmentStagePurpose.Interview, stages.Single(s => s.Name == "Interview").Purpose);
        Assert.Equal(RecruitmentStagePurpose.Offer, stages.Single(s => s.Name == "Offer").Purpose);
    }

    [Fact]
    public async Task EnsureDefaultStagesSeededAsync_Is_Idempotent_Does_Not_Duplicate_Stages_On_Second_Call()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var seeder = new RecruitmentStageSeeder(db);

        await seeder.EnsureDefaultStagesSeededAsync(companyId, Now, CancellationToken.None);
        await seeder.EnsureDefaultStagesSeededAsync(companyId, Now.AddDays(1), CancellationToken.None);

        var stages = await db.RecruitmentStages.Where(s => s.CompanyId == companyId).ToListAsync();
        Assert.Equal(6, stages.Count);
    }

    [Fact]
    public async Task EnsureDefaultStagesSeededAsync_Is_A_NoOp_When_Company_Already_Has_A_Custom_Stage()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        db.RecruitmentStages.Add(RecruitmentStage.Create(Guid.NewGuid(), companyId, "Custom Stage", 1, false, RecruitmentStageTerminalOutcome.None, Now));
        await db.SaveChangesAsync();

        await new RecruitmentStageSeeder(db).EnsureDefaultStagesSeededAsync(companyId, Now, CancellationToken.None);

        var stages = await db.RecruitmentStages.Where(s => s.CompanyId == companyId).ToListAsync();
        var stage = Assert.Single(stages);
        Assert.Equal("Custom Stage", stage.Name);
    }

    [Fact]
    public async Task EnsureDefaultStagesSeededAsync_Isolates_By_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        await new RecruitmentStageSeeder(db).EnsureDefaultStagesSeededAsync(otherCompanyId, Now, CancellationToken.None);

        await new RecruitmentStageSeeder(db).EnsureDefaultStagesSeededAsync(companyId, Now, CancellationToken.None);

        Assert.Equal(6, await db.RecruitmentStages.CountAsync(s => s.CompanyId == companyId));
        Assert.Equal(6, await db.RecruitmentStages.CountAsync(s => s.CompanyId == otherCompanyId));
    }

    [Fact]
    public void BuildDefaultStages_Returns_Six_Stages_In_Expected_Order()
    {
        var companyId = Guid.NewGuid();

        var stages = RecruitmentStageSeeder.BuildDefaultStages(companyId, Now);

        Assert.Equal(6, stages.Count);
        Assert.All(stages, s => Assert.Equal(companyId, s.CompanyId));
        Assert.Equal(
            ["Application Received", "CV Review", "Interview", "Offer", "Hired", "Rejected"],
            stages.Select(s => s.Name));
    }

    private static RecruitmentDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<RecruitmentDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
