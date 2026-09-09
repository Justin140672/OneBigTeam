using HR.Modules.Employees.Contracts;
using HR.Modules.Sickness.Domain;
using HR.Modules.Sickness.Features.UpdateSicknessRecord;
using HR.Modules.Sickness.Persistence;
using HR.Modules.Sickness.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Sickness.Tests;

// Ticket 2 (optimistic concurrency rollout): SicknessRecord.Version coverage for
// UpdateSicknessRecord. Two DbContext instances over the same EF InMemory database; context B saves
// first (bumping the store's Version), then the handler under test saves against context A with a
// stale ExpectedVersion.
public class UpdateSicknessRecordConcurrencyHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly StartDate = new(2026, 9, 1);

    private static DbContextOptions<SicknessDbContext> Options(string dbName)
        => new DbContextOptionsBuilder<SicknessDbContext>().UseInMemoryDatabase(dbName).Options;

    private static UpdateSicknessRecordHandler BuildHandler(SicknessDbContext db, FakeAuditEventPublisher audit)
        => new(db,
            new FakeClock(FixedUtcNow),
            new FakeWorkingPatternProvider(WorkingPattern.Default),
            new FakeCompanySicknessSettingsReader(false),
            new FakePublicHolidayReader(null),
            audit);

    private static async Task<(string DbName, Guid CompanyId, Guid EmployeeId, Guid CategoryId, Guid RecordId)> SeedAsync()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var category = SicknessCategory.Create(Guid.NewGuid(), companyId, "Cold", 1, now);
        var record = SicknessRecord.Create(
            Guid.NewGuid(), companyId, employeeId, category.Id, StartDate, SicknessDayPart.FullDay,
            null, null, null, "Original notes", SicknessEvidenceStatus.NotRequired, now);

        await using var seed = new SicknessDbContext(Options(dbName));
        seed.SicknessCategories.Add(category);
        seed.SicknessRecords.Add(record);
        await seed.SaveChangesAsync();

        return (dbName, companyId, employeeId, category.Id, record.Id);
    }

    private static UpdateSicknessRecordRequest Request(
        Guid companyId, Guid employeeId, Guid categoryId, Guid recordId, int? expectedVersion, string? notes = "Updated")
        => new()
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Id = recordId,
            CategoryId = categoryId,
            StartDate = StartDate,
            StartDayPart = SicknessDayPart.FullDay,
            Notes = notes,
            ExpectedVersion = expectedVersion,
        };

    [Fact]
    public void IncrementVersion_Increments_Version_By_One()
    {
        var now = new DateTimeOffset(FixedUtcNow, TimeSpan.Zero);
        var record = SicknessRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), StartDate, SicknessDayPart.FullDay,
            null, null, null, null, SicknessEvidenceStatus.NotRequired, now);
        Assert.Equal(1, record.Version);

        record.IncrementVersion();
        Assert.Equal(2, record.Version);
    }

    [Fact]
    public async Task Matching_ExpectedVersion_Succeeds_And_Bumps_Version()
    {
        var (dbName, companyId, employeeId, categoryId, recordId) = await SeedAsync();

        await using var db = new SicknessDbContext(Options(dbName));
        var result = await BuildHandler(db, new FakeAuditEventPublisher())
            .HandleAsync(Request(companyId, employeeId, categoryId, recordId, expectedVersion: 1, notes: "v2"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Version);
        Assert.Equal("v2", result.Value.Notes);
    }

    [Fact]
    public async Task Null_ExpectedVersion_Is_Rejected_As_Concurrency_And_Writes_Nothing()
    {
        var (dbName, companyId, employeeId, categoryId, recordId) = await SeedAsync();

        await using var db = new SicknessDbContext(Options(dbName));
        var result = await BuildHandler(db, new FakeAuditEventPublisher())
            .HandleAsync(Request(companyId, employeeId, categoryId, recordId, expectedVersion: null, notes: "second"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);

        await using var verify = new SicknessDbContext(Options(dbName));
        var saved = await verify.SicknessRecords.SingleAsync();
        Assert.Equal("Original notes", saved.Notes);
        Assert.Equal(1, saved.Version);
    }

    [Fact]
    public async Task Stale_ExpectedVersion_Returns_Concurrency_Failure_And_Publishes_No_Audit_Event()
    {
        var (dbName, companyId, employeeId, categoryId, recordId) = await SeedAsync();

        await using var ctxA = new SicknessDbContext(Options(dbName));
        await ctxA.SicknessRecords.SingleAsync();

        await using (var ctxB = new SicknessDbContext(Options(dbName)))
        {
            var winner = await BuildHandler(ctxB, new FakeAuditEventPublisher())
                .HandleAsync(Request(companyId, employeeId, categoryId, recordId, expectedVersion: 1, notes: "winner"), CancellationToken.None);
            Assert.True(winner.IsSuccess);
        }

        var audit = new FakeAuditEventPublisher();
        var result = await BuildHandler(ctxA, audit)
            .HandleAsync(Request(companyId, employeeId, categoryId, recordId, expectedVersion: 1, notes: "loser"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("concurrency", result.Error.Code);
        Assert.Empty(audit.PublishedEvents);

        await using var verify = new SicknessDbContext(Options(dbName));
        var saved = await verify.SicknessRecords.SingleAsync();
        Assert.Equal("winner", saved.Notes);
        Assert.Equal(2, saved.Version);
    }
}
