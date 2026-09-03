using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using HR.Modules.Documents.Services;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

/// <summary>
/// TEST-005. Deterministic-replay coverage for
/// <see cref="SharedCompanyDocumentAcknowledgementHistoryReplayer"/> used by the Employees
/// timeline backfill.
/// </summary>
public class SharedCompanyDocumentAcknowledgementHistoryReplayerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);

    private static DocumentsDbContext BuildContext(string name) =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(name)
            .Options);

    private static async Task<SharedCompanyDocument> SeedDocAsync(DocumentsDbContext db, Guid companyId, string title)
    {
        var category = CompanyDocumentCategory.Create(Guid.NewGuid(), companyId, "Policy", Now);
        db.CompanyDocumentCategories.Add(category);
        var doc = SharedCompanyDocument.Create(
            Guid.NewGuid(), companyId, title, null, category.Id, "key/doc.pdf",
            "doc.pdf", 100, "application/pdf", null, null, SharedCompanyDocumentReviewFrequency.None, null, null,
            true, null, null, Guid.NewGuid(), Now);
        doc.Publish(Guid.NewGuid(), Now);
        db.SharedCompanyDocuments.Add(doc);
        await db.SaveChangesAsync();
        return doc;
    }

    private static async Task SeedAckAsync(DocumentsDbContext db, Guid companyId, Guid docId, Guid employeeId, DateTimeOffset at)
    {
        db.SharedCompanyDocumentAcknowledgements.Add(SharedCompanyDocumentAcknowledgement.Create(
            Guid.NewGuid(), companyId, docId, employeeId, 1, "Statement", null, true, at));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Replay_Emits_One_Event_Per_Acknowledgement_Scoped_To_The_Requested_Company()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        await using (var seed = BuildContext(dbName))
        {
            var doc = await SeedDocAsync(seed, companyId, "Handbook");
            await SeedAckAsync(seed, companyId, doc.Id, Guid.NewGuid(), Now);
            await SeedAckAsync(seed, companyId, doc.Id, Guid.NewGuid(), Now.AddMinutes(1));

            var otherDoc = await SeedDocAsync(seed, otherCompanyId, "Other Handbook");
            await SeedAckAsync(seed, otherCompanyId, otherDoc.Id, Guid.NewGuid(), Now);
        }

        await using var db = BuildContext(dbName);
        var publisher = new CapturingIntegrationEventPublisher();
        var replayer = new SharedCompanyDocumentAcknowledgementHistoryReplayer(db, publisher);

        var count = await replayer.ReplaySharedCompanyDocumentAcknowledgedAsync(companyId, CancellationToken.None);

        Assert.Equal(2, count);
        var events = publisher.Published.OfType<SharedCompanyDocumentAcknowledgedIntegrationEvent>().ToList();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(companyId, e.CompanyId));
        Assert.All(events, e => Assert.Equal("Handbook", e.DocumentTitle));
    }

    [Fact]
    public async Task Replay_Run_Twice_Produces_The_Same_Set_Of_Events_Both_Times()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();
        var e1 = Guid.NewGuid();
        var e2 = Guid.NewGuid();
        var e3 = Guid.NewGuid();

        await using (var seed = BuildContext(dbName))
        {
            var doc = await SeedDocAsync(seed, companyId, "Handbook");
            await SeedAckAsync(seed, companyId, doc.Id, e1, Now);
            await SeedAckAsync(seed, companyId, doc.Id, e2, Now.AddHours(1));
            await SeedAckAsync(seed, companyId, doc.Id, e3, Now.AddHours(2));
        }

        static (Guid, Guid, DateTimeOffset) Key(SharedCompanyDocumentAcknowledgedIntegrationEvent e)
            => (e.EmployeeId, e.DocumentId, e.OccurredAt);

        List<(Guid, Guid, DateTimeOffset)> Run()
        {
            using var db = BuildContext(dbName);
            var publisher = new CapturingIntegrationEventPublisher();
            var replayer = new SharedCompanyDocumentAcknowledgementHistoryReplayer(db, publisher);
            replayer.ReplaySharedCompanyDocumentAcknowledgedAsync(companyId, CancellationToken.None).GetAwaiter().GetResult();
            return publisher.Published
                .OfType<SharedCompanyDocumentAcknowledgedIntegrationEvent>()
                .Select(Key)
                .OrderBy(k => k.Item3)
                .ToList();
        }

        var first = Run();
        var second = Run();

        Assert.Equal(3, first.Count);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Replay_With_No_Acknowledgements_Returns_Zero_And_Emits_Nothing()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();

        await using (var seed = BuildContext(dbName))
            await SeedDocAsync(seed, companyId, "Handbook");

        await using var db = BuildContext(dbName);
        var publisher = new CapturingIntegrationEventPublisher();
        var replayer = new SharedCompanyDocumentAcknowledgementHistoryReplayer(db, publisher);

        var count = await replayer.ReplaySharedCompanyDocumentAcknowledgedAsync(companyId, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task Replay_Skips_An_Acknowledgement_Whose_Document_Row_Is_Missing()
    {
        var dbName = Guid.NewGuid().ToString("N");
        var companyId = Guid.NewGuid();

        await using (var seed = BuildContext(dbName))
        {
            // Acknowledgement rows with no matching SharedCompanyDocument (inner join drops them).
            await SeedAckAsync(seed, companyId, Guid.NewGuid(), Guid.NewGuid(), Now);
        }

        await using var db = BuildContext(dbName);
        var publisher = new CapturingIntegrationEventPublisher();
        var replayer = new SharedCompanyDocumentAcknowledgementHistoryReplayer(db, publisher);

        var count = await replayer.ReplaySharedCompanyDocumentAcknowledgedAsync(companyId, CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Empty(publisher.Published);
    }
}
