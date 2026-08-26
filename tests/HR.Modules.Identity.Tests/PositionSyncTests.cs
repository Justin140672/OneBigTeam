using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class PositionSyncTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddDays(1);

    [Fact]
    public async Task EnsureExistsAsync_Creates_New_Position_When_None_Exists()
    {
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Software Developer", null, null, true, null, null),
        };
        var reader = new FakePositionProfileReader(summaries: summaries);

        await using (var db = fixture.BuildContext())
        {
            var sync = new PositionSync(db, reader);
            var result = await sync.EnsureExistsAsync(companyId, positionProfileId, Now, CancellationToken.None);
            Assert.NotNull(result);
            await db.SaveChangesAsync();
        }

        await using var db2 = fixture.BuildContext();
        var saved = await db2.Positions.SingleAsync(p => p.Id == positionProfileId);
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal("Software Developer", saved.Name);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task EnsureExistsAsync_Renames_And_Reactivates_Existing_Position()
    {
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            var existing = Position.Create(positionProfileId, companyId, "Old Name", Now);
            existing.Deactivate(Now);
            db.Positions.Add(existing);
            await db.SaveChangesAsync();
        }

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "New Name", null, null, true, null, null),
        };
        var reader = new FakePositionProfileReader(summaries: summaries);

        await using (var db = fixture.BuildContext())
        {
            var sync = new PositionSync(db, reader);
            await sync.EnsureExistsAsync(companyId, positionProfileId, Later, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using var db2 = fixture.BuildContext();
        var saved = await db2.Positions.SingleAsync(p => p.Id == positionProfileId);
        Assert.Equal("New Name", saved.Name);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task EnsureExistsAsync_Deactivates_Existing_Position_When_Profile_Now_Inactive()
    {
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Positions.Add(Position.Create(positionProfileId, companyId, "Some Position", Now));
            await db.SaveChangesAsync();
        }

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Some Position", null, null, false, null, null),
        };
        var reader = new FakePositionProfileReader(summaries: summaries);

        await using (var db = fixture.BuildContext())
        {
            var sync = new PositionSync(db, reader);
            await sync.EnsureExistsAsync(companyId, positionProfileId, Later, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using var db2 = fixture.BuildContext();
        var saved = await db2.Positions.SingleAsync(p => p.Id == positionProfileId);
        Assert.False(saved.IsActive);
    }

    [Fact]
    public async Task EnsureExistsAsync_Returns_Null_And_Creates_Nothing_When_Profile_Cannot_Be_Resolved()
    {
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();
        var reader = new FakePositionProfileReader(summaries: null); // GetSummaryAsync -> null for everything

        await using var db = fixture.BuildContext();
        var sync = new PositionSync(db, reader);

        var result = await sync.EnsureExistsAsync(companyId, positionProfileId, Now, CancellationToken.None);

        Assert.Null(result);
        Assert.False(await db.Positions.AnyAsync(p => p.Id == positionProfileId));
    }

    [Fact]
    public async Task EnsureExistsAsync_Leaves_Existing_Row_Untouched_When_Profile_Cannot_Be_Resolved()
    {
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        await using (var db = fixture.BuildContext())
        {
            db.Positions.Add(Position.Create(positionProfileId, companyId, "Untouched", Now));
            await db.SaveChangesAsync();
        }

        var reader = new FakePositionProfileReader(summaries: null);

        await using (var db = fixture.BuildContext())
        {
            var sync = new PositionSync(db, reader);
            var result = await sync.EnsureExistsAsync(companyId, positionProfileId, Later, CancellationToken.None);
            Assert.NotNull(result); // returns the existing row unchanged
            await db.SaveChangesAsync();
        }

        await using var db2 = fixture.BuildContext();
        var saved = await db2.Positions.SingleAsync(p => p.Id == positionProfileId);
        Assert.Equal("Untouched", saved.Name);
        Assert.Equal(Now, saved.UpdatedAt); // not touched by the second call
    }
}
