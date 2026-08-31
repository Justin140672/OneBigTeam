using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.GetTeamSicknessToday;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

/// <summary>
/// DSH-02: the team-sickness-today widget scopes to the manager's entire reporting sub-tree
/// (direct and indirect reports) via <c>GetAllDescendantIdsAsync</c>. A peer / unrelated manager's
/// active sickness records are excluded. See
/// specifications/architecture/11-manager-hierarchy-scope.md.
/// </summary>
public class GetTeamSicknessTodayHierarchyScopeTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly StartDate = new(2026, 7, 1);

    private static SicknessDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SicknessDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static async Task<Guid> SeedCategory(SicknessDbContext db, Guid companyId)
    {
        var category = SicknessCategory.Create(Guid.NewGuid(), companyId, "Cold", 1, Now);
        db.SicknessCategories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    }

    private static SicknessRecord ActiveRecord(Guid companyId, Guid employeeId, Guid categoryId) =>
        SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, categoryId, StartDate, SicknessDayPart.FullDay,
            null, null, null, null, SicknessEvidenceStatus.NotRequired, Now);

    [Fact]
    public async Task Handler_Includes_Indirect_Report_Excludes_Peers_Keeps_Direct_Report()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);

        var seniorManager = Guid.NewGuid();
        var lineManager = Guid.NewGuid();
        var directReport = Guid.NewGuid();
        var indirectReport = Guid.NewGuid();
        var peerReport = Guid.NewGuid();

        var reader = FakeDirectReportsReader.WithHierarchy(
            (seniorManager, lineManager),
            (seniorManager, directReport),
            (lineManager, indirectReport),
            (Guid.NewGuid(), peerReport));

        db.SicknessRecords.AddRange(
            ActiveRecord(companyId, directReport, categoryId),
            ActiveRecord(companyId, indirectReport, categoryId),
            ActiveRecord(companyId, peerReport, categoryId));
        await db.SaveChangesAsync();

        var handler = new GetTeamSicknessTodayHandler(db, reader);

        var result = await handler.HandleAsync(
            new GetTeamSicknessTodayRequest(companyId, seniorManager), CancellationToken.None);

        var ids = result.Items.Select(i => i.EmployeeId).ToHashSet();
        Assert.Contains(directReport, ids);
        Assert.Contains(indirectReport, ids);
        Assert.DoesNotContain(peerReport, ids);
    }

    [Fact]
    public async Task Handler_Returns_Empty_For_Manager_With_Empty_Subtree()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var categoryId = await SeedCategory(db, companyId);
        db.SicknessRecords.Add(ActiveRecord(companyId, Guid.NewGuid(), categoryId));
        await db.SaveChangesAsync();

        var handler = new GetTeamSicknessTodayHandler(db, FakeDirectReportsReader.WithHierarchy());

        var result = await handler.HandleAsync(
            new GetTeamSicknessTodayRequest(companyId, Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(result.Items);
    }
}
