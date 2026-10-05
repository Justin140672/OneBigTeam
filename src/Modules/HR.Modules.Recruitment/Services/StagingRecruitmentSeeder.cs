using HR.Infrastructure.Abstractions;
using HR.Modules.Recruitment.Domain;
using HR.Modules.Recruitment.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Recruitment.Services;

internal sealed record StagingVacancyDefinition(
    int Number,
    string PositionName,
    string HiringManagerName,
    string AdvertTitle,
    string AdvertDescription);

internal static class StagingRecruitmentSeeder
{
    public static readonly IReadOnlyList<StagingVacancyDefinition> Vacancies =
    [
        new(1, "Marketing Coordinator", "David Park", "Marketing Coordinator",
            "Support our sales and marketing team with campaign coordination, event logistics and content scheduling."),
        new(2, "Software Engineer", "James Okafor", "Software Engineer",
            "Join the platform engineering team to build and maintain core product features."),
    ];

    private static readonly Guid PermanentEmploymentTypeId = new("5A000004-0000-0000-0000-000000000001");

    public static Guid VacancyId(int number) => new($"5A0000E0-0000-0000-0000-{number:D12}");

    public static async Task SeedAsync(
        IServiceProvider services,
        IReadOnlyDictionary<string, Guid> positionProfileIdsByName,
        IReadOnlyDictionary<string, Guid> employeeIdsByName)
    {
        var db = services.GetRequiredService<RecruitmentDbContext>();
        var stageSeeder = services.GetRequiredService<RecruitmentStageSeeder>();
        var companyId = StagingSeedOptions.CompanyId;
        var now = DateTimeOffset.UtcNow;

        await stageSeeder.EnsureDefaultStagesSeededAsync(companyId, now, CancellationToken.None);

        if (await db.Vacancies.AnyAsync(v => v.CompanyId == companyId))
        {
            return;
        }

        var openedAt = DateOnly.FromDateTime(now.UtcDateTime.AddDays(-7));

        foreach (var definition in Vacancies)
        {
            if (!positionProfileIdsByName.TryGetValue(definition.PositionName, out var positionProfileId))
            {
                throw new InvalidOperationException(
                    $"Staging vacancy position profile '{definition.PositionName}' was not found.");
            }

            if (!employeeIdsByName.TryGetValue(definition.HiringManagerName, out var hiringManagerId))
            {
                throw new InvalidOperationException(
                    $"Staging vacancy hiring manager '{definition.HiringManagerName}' was not found.");
            }

            var vacancy = Vacancy.Create(
                VacancyId(definition.Number), companyId, positionProfileId,
                definition.AdvertTitle, definition.AdvertDescription, hiringManagerId, now,
                isAdvertisedInternally: true,
                employmentTypeId: PermanentEmploymentTypeId);
            vacancy.Open(now, openedAt);
            db.Vacancies.Add(vacancy);
        }

        await db.SaveChangesAsync();
    }
}
