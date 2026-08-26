using HR.Modules.Employees.Contracts;
using HR.Modules.Identity.Features.OnPositionProfileUpserted;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Tests;

[Collection("IdentityDatabase")]
public class OnPositionProfileUpsertedHandlerTests(IdentityDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task HandleAsync_Syncs_And_Persists_A_New_Position_From_The_Event_Data()
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
            var handler = new Handler(db, new PositionSync(db, reader));
            await handler.HandleAsync(
                new PositionProfileUpsertedIntegrationEvent(companyId, positionProfileId, "Software Developer", true, Now),
                CancellationToken.None);
        }

        await using var db2 = fixture.BuildContext();
        var saved = await db2.Positions.SingleAsync(p => p.Id == positionProfileId);
        Assert.Equal(companyId, saved.CompanyId);
        Assert.Equal("Software Developer", saved.Name);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task HandleAsync_Deactivates_Existing_Position_When_Event_Reports_Inactive()
    {
        var companyId = Guid.NewGuid();
        var positionProfileId = Guid.NewGuid();

        var summaries = new Dictionary<Guid, PositionProfileSummary>
        {
            [positionProfileId] = new(positionProfileId, "Software Developer", null, null, false, null, null),
        };
        var reader = new FakePositionProfileReader(summaries: summaries);

        await using (var db = fixture.BuildContext())
        {
            db.Positions.Add(Domain.Position.Create(positionProfileId, companyId, "Software Developer", Now));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.BuildContext())
        {
            var handler = new Handler(db, new PositionSync(db, reader));
            await handler.HandleAsync(
                new PositionProfileUpsertedIntegrationEvent(companyId, positionProfileId, "Software Developer", false, Now.AddDays(1)),
                CancellationToken.None);
        }

        await using var db2 = fixture.BuildContext();
        var saved = await db2.Positions.SingleAsync(p => p.Id == positionProfileId);
        Assert.False(saved.IsActive);
    }
}
