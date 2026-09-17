using System.Net;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging.Abstractions;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class SupportServiceTests
{
    private static SupportService BuildService(HttpMessageHandler handler) =>
        new(BuildFactory(handler), NullLogger<SupportService>.Instance);

    // ── ListSupportRequestsAsync (representative read) ───────────────────────────

    [Fact]
    public async Task ListSupportRequestsAsync_Returns_Value_When_Api_Returns_Ok()
    {
        var response = new List<SupportRequestListItem>();
        var service = BuildService(new JsonResponseHandler(HttpStatusCode.OK, response));

        var result = await service.ListSupportRequestsAsync(Guid.NewGuid());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ListSupportRequestsAsync_Returns_Null_When_Api_Returns_Unauthorized()
    {
        var service = BuildService(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));

        var result = await service.ListSupportRequestsAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task ListSupportRequestsAsync_Returns_Null_When_Api_Returns_Forbidden()
    {
        var service = BuildService(new JsonResponseHandler(HttpStatusCode.Forbidden, null));

        var result = await service.ListSupportRequestsAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task ListSupportRequestsAsync_Propagates_Cancellation_When_Token_Already_Cancelled()
    {
        var service = BuildService(new JsonResponseHandler(HttpStatusCode.OK, new List<SupportRequestListItem>()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ListSupportRequestsAsync(Guid.NewGuid(), cancellationToken: cts.Token));
    }

    // ── GetSupportRequestAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetSupportRequestAsync_Returns_Null_When_Api_Returns_NotFound()
    {
        var service = BuildService(new JsonResponseHandler(HttpStatusCode.NotFound, new { error = "Support request not found." }));

        var result = await service.GetSupportRequestAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    // ── SubmitSupportRequestAsync (write) ────────────────────────────────────────

    [Fact]
    public async Task SubmitSupportRequestAsync_Returns_Result_When_Api_Returns_Created()
    {
        var response = new SubmitSupportRequestResult(Guid.NewGuid(), "SR-0001");
        var service = BuildService(new JsonResponseHandler(HttpStatusCode.Created, response));

        var (result, error) = await service.SubmitSupportRequestAsync(
            Guid.NewGuid(), "ReportProblem", "Title", "Description", "Medium", false,
            null, null, null, null, null, Array.Empty<IBrowserFile>());

        Assert.NotNull(result);
        Assert.Null(error);
    }

    [Fact]
    public async Task SubmitSupportRequestAsync_Returns_ValidationMessage_When_Api_Returns_BadRequest()
    {
        var service = BuildService(new JsonResponseHandler(HttpStatusCode.BadRequest, new { error = "Title is required." }));

        var (result, error) = await service.SubmitSupportRequestAsync(
            Guid.NewGuid(), "ReportProblem", "Title", "Description", "Medium", false,
            null, null, null, null, null, Array.Empty<IBrowserFile>());

        Assert.Null(result);
        Assert.Equal("Title is required.", error);
    }

    [Fact]
    public async Task SubmitSupportRequestAsync_Returns_Failure_When_Api_Returns_Unauthorized()
    {
        var service = BuildService(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));

        var (result, error) = await service.SubmitSupportRequestAsync(
            Guid.NewGuid(), "ReportProblem", "Title", "Description", "Medium", false,
            null, null, null, null, null, Array.Empty<IBrowserFile>());

        Assert.Null(result);
        Assert.Equal("Your session has expired. Please sign in again.", error);
    }

    [Fact]
    public async Task SubmitSupportRequestAsync_Returns_Controlled_Failure_When_Success_Body_Is_Malformed()
    {
        var service = BuildService(new MalformedJsonHandler(HttpStatusCode.Created));

        var (result, error) = await service.SubmitSupportRequestAsync(
            Guid.NewGuid(), "ReportProblem", "Title", "Description", "Medium", false,
            null, null, null, null, null, Array.Empty<IBrowserFile>());

        Assert.Null(result);
        Assert.NotNull(error);
    }

    // ── AddResponseAsync (write) ──────────────────────────────────────────────────

    [Fact]
    public async Task AddResponseAsync_Returns_Result_When_Api_Returns_Created()
    {
        var response = new AddSupportResponseResult(Guid.NewGuid(), true, DateTimeOffset.UtcNow);
        var service = BuildService(new JsonResponseHandler(HttpStatusCode.Created, response));

        var (result, error) = await service.AddResponseAsync(Guid.NewGuid(), Guid.NewGuid(), "<p>Reply</p>", Array.Empty<IBrowserFile>());

        Assert.NotNull(result);
        Assert.Null(error);
    }

    [Fact]
    public async Task AddResponseAsync_Returns_Failure_When_Api_Returns_NotFound()
    {
        var service = BuildService(new JsonResponseHandler(HttpStatusCode.NotFound, new { error = "Support request not found." }));

        var (result, error) = await service.AddResponseAsync(Guid.NewGuid(), Guid.NewGuid(), "<p>Reply</p>", Array.Empty<IBrowserFile>());

        Assert.Null(result);
        Assert.Equal("Support request not found.", error);
    }
}
