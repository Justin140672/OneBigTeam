using HR.Modules.Support.Domain;
using HR.Modules.Support.Features.ListSupportRequests;
using HR.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Tests;

public class ListSupportRequestsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private static SupportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SupportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SupportRequest CreateRequest(
        Guid companyId,
        string reference,
        SupportRequestStatus status = SupportRequestStatus.Submitted,
        DateTimeOffset? now = null)
    {
        var entity = SupportRequest.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), null,
            SupportRequestType.AskQuestion, "Title " + reference, "Description", SupportRequestPriority.Low,
            reference, null, null, null, false, null, null, now ?? Now);

        if (status != SupportRequestStatus.Submitted)
            entity.ChangeStatus(status, now ?? Now);

        return entity;
    }

    [Fact]
    public async Task HandleAsync_Returns_Only_Requests_For_The_Given_Company()
    {
        await using var db = BuildContext();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        db.SupportRequests.Add(CreateRequest(companyA, "SUP-A-1"));
        db.SupportRequests.Add(CreateRequest(companyB, "SUP-B-1"));
        await db.SaveChangesAsync();

        var handler = new ListSupportRequestsHandler(db);
        var result = await handler.HandleAsync(new ListSupportRequestsRequest { CompanyId = companyA }, CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal("SUP-A-1", item.ReferenceNumber);
    }

    [Fact]
    public async Task HandleAsync_Filters_By_Status_When_Provided()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        db.SupportRequests.Add(CreateRequest(companyId, "SUP-1", SupportRequestStatus.Submitted));
        db.SupportRequests.Add(CreateRequest(companyId, "SUP-2", SupportRequestStatus.Resolved));
        await db.SaveChangesAsync();

        var handler = new ListSupportRequestsHandler(db);
        var result = await handler.HandleAsync(
            new ListSupportRequestsRequest { CompanyId = companyId, Status = SupportRequestStatus.Resolved },
            CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal("SUP-2", item.ReferenceNumber);
    }

    [Fact]
    public async Task HandleAsync_Returns_All_Company_Requests_When_Status_Filter_Is_Null()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        db.SupportRequests.Add(CreateRequest(companyId, "SUP-1", SupportRequestStatus.Submitted));
        db.SupportRequests.Add(CreateRequest(companyId, "SUP-2", SupportRequestStatus.Resolved));
        await db.SaveChangesAsync();

        var handler = new ListSupportRequestsHandler(db);
        var result = await handler.HandleAsync(new ListSupportRequestsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task HandleAsync_Returns_Empty_List_When_No_Matches()
    {
        await using var db = BuildContext();
        var handler = new ListSupportRequestsHandler(db);

        var result = await handler.HandleAsync(new ListSupportRequestsRequest { CompanyId = Guid.NewGuid() }, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_Includes_Latest_Response_Snippet()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = CreateRequest(companyId, "SUP-1");
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        db.SupportResponses.Add(SupportResponse.Create(
            Guid.NewGuid(), request.Id, companyId, Guid.NewGuid(), true, "First reply", Now));
        db.SupportResponses.Add(SupportResponse.Create(
            Guid.NewGuid(), request.Id, companyId, Guid.NewGuid(), true, "Second reply, the latest one", Now.AddMinutes(5)));
        await db.SaveChangesAsync();

        var handler = new ListSupportRequestsHandler(db);
        var result = await handler.HandleAsync(new ListSupportRequestsRequest { CompanyId = companyId }, CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal("Second reply, the latest one", item.LatestResponseSnippet);
    }

    [Fact]
    public async Task HandleAsync_LatestResponseSnippet_Is_Null_When_No_Responses()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        db.SupportRequests.Add(CreateRequest(companyId, "SUP-1"));
        await db.SaveChangesAsync();

        var handler = new ListSupportRequestsHandler(db);
        var result = await handler.HandleAsync(new ListSupportRequestsRequest { CompanyId = companyId }, CancellationToken.None);

        Assert.Null(Assert.Single(result).LatestResponseSnippet);
    }

    [Fact]
    public async Task HandleAsync_Does_Not_Truncate_Snippet_When_Exactly_160_Characters()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = CreateRequest(companyId, "SUP-1");
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var body = new string('A', 160);
        db.SupportResponses.Add(SupportResponse.Create(
            Guid.NewGuid(), request.Id, companyId, Guid.NewGuid(), true, body, Now));
        await db.SaveChangesAsync();

        var handler = new ListSupportRequestsHandler(db);
        var result = await handler.HandleAsync(new ListSupportRequestsRequest { CompanyId = companyId }, CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal(body, item.LatestResponseSnippet);
    }

    [Fact]
    public async Task HandleAsync_Truncates_Snippet_When_161_Characters()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = CreateRequest(companyId, "SUP-1");
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var body = new string('A', 161);
        db.SupportResponses.Add(SupportResponse.Create(
            Guid.NewGuid(), request.Id, companyId, Guid.NewGuid(), true, body, Now));
        await db.SaveChangesAsync();

        var handler = new ListSupportRequestsHandler(db);
        var result = await handler.HandleAsync(new ListSupportRequestsRequest { CompanyId = companyId }, CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal(new string('A', 160) + "…", item.LatestResponseSnippet);
    }
}
