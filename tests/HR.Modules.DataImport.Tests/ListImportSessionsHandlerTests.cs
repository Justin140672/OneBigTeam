using HR.Modules.DataImport.Domain;
using HR.Modules.DataImport.Features.ListImportSessions;
using HR.Modules.DataImport.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.DataImport.Tests;

public class ListImportSessionsHandlerTests
{
    private static DataImportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DataImportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static ImportSession CreateSession(Guid companyId, DateTimeOffset createdAt) =>
        ImportSession.Create(
            Guid.NewGuid(),
            companyId,
            "Employees",
            $"employees-{createdAt.Ticks}.csv",
            totalRows: 1,
            Guid.NewGuid(),
            $"sessions/{Guid.NewGuid():N}/employees.csv",
            "text/csv",
            createdAt);

    [Fact]
    public async Task HandleAsync_Returns_Sessions_Ordered_Most_Recent_First()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();

        var older = CreateSession(companyId, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = CreateSession(companyId, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        var middle = CreateSession(companyId, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

        db.ImportSessions.AddRange(older, newer, middle);
        await db.SaveChangesAsync();

        var handler = new ListImportSessionsHandler(db);

        var result = await handler.HandleAsync(
            new ListImportSessionsRequest { CompanyId = companyId },
            CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Equal(newer.Id, result[0].Id);
        Assert.Equal(middle.Id, result[1].Id);
        Assert.Equal(older.Id, result[2].Id);
    }

    [Fact]
    public async Task HandleAsync_Excludes_Sessions_Belonging_To_A_Different_Company()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        var mine = CreateSession(companyId, DateTimeOffset.UtcNow);
        var theirs = CreateSession(otherCompanyId, DateTimeOffset.UtcNow);

        db.ImportSessions.AddRange(mine, theirs);
        await db.SaveChangesAsync();

        var handler = new ListImportSessionsHandler(db);

        var result = await handler.HandleAsync(
            new ListImportSessionsRequest { CompanyId = companyId },
            CancellationToken.None);

        var session = Assert.Single(result);
        Assert.Equal(mine.Id, session.Id);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_For_Company_With_No_Sessions()
    {
        await using var db = BuildContext();

        var handler = new ListImportSessionsHandler(db);

        var result = await handler.HandleAsync(
            new ListImportSessionsRequest { CompanyId = Guid.NewGuid() },
            CancellationToken.None);

        Assert.Empty(result);
    }
}
