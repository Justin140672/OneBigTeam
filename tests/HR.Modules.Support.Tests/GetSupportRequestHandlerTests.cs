using HR.Modules.Support.Domain;
using HR.Modules.Support.Features.GetSupportRequest;
using HR.Modules.Support.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Tests;

public class GetSupportRequestHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);

    private static SupportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SupportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static SupportRequest CreateRequest(Guid companyId) =>
        SupportRequest.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), null,
            SupportRequestType.AskQuestion, "Title", "Description", SupportRequestPriority.Low,
            "SUP-1", "/page", "Chrome", "1.0", true, "{}", "corr-1", Now);

    [Fact]
    public async Task HandleAsync_Returns_Request_With_Attachments_And_Responses()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = CreateRequest(companyId);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        db.SupportAttachments.Add(SupportAttachment.Create(
            Guid.NewGuid(), request.Id, companyId, "key", "file.png", "image/png", 100, Guid.NewGuid(), Now));

        var response = SupportResponse.Create(Guid.NewGuid(), request.Id, companyId, Guid.NewGuid(), true, "Reply", Now);
        db.SupportResponses.Add(response);
        db.SupportResponseAttachments.Add(SupportResponseAttachment.Create(
            Guid.NewGuid(), response.Id, companyId, "key2", "reply-file.png", "image/png", Now));
        await db.SaveChangesAsync();

        var handler = new GetSupportRequestHandler(db);
        var result = await handler.HandleAsync(new GetSupportRequestRequest { CompanyId = companyId, Id = request.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("SUP-1", result.Value!.ReferenceNumber);
        Assert.Single(result.Value.Attachments);
        Assert.Equal("file.png", result.Value.Attachments[0].FileName);
        Assert.Single(result.Value.Responses);
        Assert.Equal("Reply", result.Value.Responses[0].BodyHtml);
        Assert.Single(result.Value.Responses[0].Attachments);
        Assert.Equal("reply-file.png", result.Value.Responses[0].Attachments[0].FileName);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Id_Does_Not_Exist()
    {
        await using var db = BuildContext();
        var handler = new GetSupportRequestHandler(db);

        var result = await handler.HandleAsync(
            new GetSupportRequestRequest { CompanyId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Belongs_To_Different_Company()
    {
        await using var db = BuildContext();
        var request = CreateRequest(Guid.NewGuid());
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new GetSupportRequestHandler(db);
        var result = await handler.HandleAsync(
            new GetSupportRequestRequest { CompanyId = Guid.NewGuid(), Id = request.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }
}
