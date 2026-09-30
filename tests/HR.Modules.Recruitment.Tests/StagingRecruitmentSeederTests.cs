using HR.Modules.Recruitment.Services;

namespace HR.Modules.Recruitment.Tests;

public class StagingRecruitmentSeederTests
{
    [Fact]
    public void Vacancies_AreTwoWithUniqueIds()
    {
        Assert.Equal(2, StagingRecruitmentSeeder.Vacancies.Count);
        Assert.Equal(
            StagingRecruitmentSeeder.Vacancies.Count,
            StagingRecruitmentSeeder.Vacancies.Select(v => StagingRecruitmentSeeder.VacancyId(v.Number)).Distinct().Count());
    }

    [Fact]
    public void Vacancies_TargetMarketingCoordinatorAndSoftwareEngineerWithExpectedHiringManagers()
    {
        var marketing = Assert.Single(StagingRecruitmentSeeder.Vacancies, v => v.PositionName == "Marketing Coordinator");
        var engineer = Assert.Single(StagingRecruitmentSeeder.Vacancies, v => v.PositionName == "Software Engineer");

        Assert.Equal("David Park", marketing.HiringManagerName);
        Assert.Equal("James Okafor", engineer.HiringManagerName);
        Assert.All(StagingRecruitmentSeeder.Vacancies, v =>
        {
            Assert.False(string.IsNullOrWhiteSpace(v.AdvertTitle));
            Assert.False(string.IsNullOrWhiteSpace(v.AdvertDescription));
        });
    }
}
