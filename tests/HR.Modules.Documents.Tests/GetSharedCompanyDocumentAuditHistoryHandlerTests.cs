using HR.Modules.Documents.Features.GetSharedCompanyDocumentAuditHistory;
using HR.Modules.Documents.Tests.Infrastructure;
using HR.SharedKernel;

namespace HR.Modules.Documents.Tests;

public class GetSharedCompanyDocumentAuditHistoryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 10, 0, 0, TimeSpan.Zero);
    private const string EntityType = "SharedCompanyDocument";

    [Fact]
    public async Task HandleAsync_Maps_OccurredAt_And_Action_From_Summary()
    {
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "shared_company_document.acknowledged", EntityType, null, null,
                "Document 'Policy' acknowledged (v1)", null, null, null, documentId)
        ]);
        var handler = new GetSharedCompanyDocumentAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, documentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal(Now, item.OccurredAt);
        Assert.Equal("Document 'Policy' acknowledged (v1)", item.Action);
    }

    [Fact]
    public async Task HandleAsync_Falls_Back_To_EventType_As_Action_When_Summary_Is_Null()
    {
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "shared_company_document.archived", EntityType, null, null,
                null, null, null, null, documentId)
        ]);
        var handler = new GetSharedCompanyDocumentAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, documentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("shared_company_document.archived", item.Action);
    }

    [Fact]
    public async Task HandleAsync_Resolves_Known_ActorEmployeeId_To_Full_Name()
    {
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "shared_company_document.published", EntityType, null, actorId,
                "Document published", null, null, null, documentId)
        ]);
        var names = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [actorId] = "Ada Acknowledger" });
        var handler = new GetSharedCompanyDocumentAuditHistoryHandler(reader, names);

        var result = await handler.HandleAsync(companyId, documentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Ada Acknowledger", item.User);
    }

    [Fact]
    public async Task HandleAsync_Returns_System_When_ActorEmployeeId_Is_Null()
    {
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "shared_company_document.expired", EntityType, null, null,
                "Document expired", null, null, null, documentId)
        ]);
        var handler = new GetSharedCompanyDocumentAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, documentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("System", item.User);
    }

    [Fact]
    public async Task HandleAsync_Returns_Unknown_When_ActorEmployeeId_Not_Found_In_Name_Reader()
    {
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "shared_company_document.published", EntityType, null, actorId,
                "Document published", null, null, null, documentId)
        ]);
        var handler = new GetSharedCompanyDocumentAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, documentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Unknown", item.User);
    }

    [Fact]
    public async Task HandleAsync_Builds_Before_And_After_Field_Changes_From_Json()
    {
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(
                Now, "shared_company_document.metadata_updated", EntityType, null, null,
                "Document metadata updated",
                """{"Title":"Old Title"}""", """{"Title":"New Title"}""", null, documentId)
        ]);
        var handler = new GetSharedCompanyDocumentAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, documentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        var change = Assert.Single(item.Changes);
        Assert.Equal("Title", change.Field);
        Assert.Equal("Old Title", change.Before);
        Assert.Equal("New Title", change.After);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_Changes_When_BeforeJson_And_AfterJson_Are_Both_Null()
    {
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "shared_company_document.expired", EntityType, null, null,
                "Document expired", null, null, null, documentId)
        ]);
        var handler = new GetSharedCompanyDocumentAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, documentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Empty(item.Changes);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_Response_When_No_Entries_Exist_For_The_Document()
    {
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([]);
        var handler = new GetSharedCompanyDocumentAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, documentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Leak_Entries_Scoped_To_A_Different_Document()
    {
        var companyId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var otherDocumentId = Guid.NewGuid();

        var reader = new FakeAuditHistoryReader([
            new AuditHistoryEntry(Now, "shared_company_document.published", EntityType, null, null,
                "Document published", null, null, null, documentId),
            new AuditHistoryEntry(Now, "shared_company_document.published", EntityType, null, null,
                "Other document published", null, null, null, otherDocumentId)
        ]);
        var handler = new GetSharedCompanyDocumentAuditHistoryHandler(reader, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(companyId, documentId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value!.Items);
        Assert.Equal("Document published", item.Action);
    }
}
