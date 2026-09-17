using System.Net;
using HR.Web.Models;
using HR.Web.Services;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class AssetServiceTests
{
    private static UpdateAssetRequest SampleRequest(Guid companyId, Guid id) =>
        new(companyId, id, "AST-001", Guid.NewGuid(), "MacBook Pro", "Apple", "M3", "SN123", null, null, 2);

    // ── UpdateAsync(ApiSaveResult) ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_Returns_Ok_When_Api_Returns_Success()
    {
        var response = new UpdateAssetResponse(Guid.NewGuid(), Guid.NewGuid(), "AST-001", Guid.NewGuid(), "MacBook Pro", null, null, null, null, null, "Available", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Version: 3);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new AssetService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(),
            new AssetEditModel { AssetNumber = "AST-001", CategoryId = Guid.NewGuid(), Name = "MacBook Pro" }, expectedVersion: 2);

        Assert.True(result.Success);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Flags_ConcurrencyConflict_On_409_With_Concurrency_Code()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "Changed by someone else.", code = "concurrency" }));
        var service = new AssetService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(),
            new AssetEditModel { AssetNumber = "AST-001", CategoryId = Guid.NewGuid(), Name = "MacBook Pro" }, expectedVersion: 2);

        Assert.False(result.Success);
        Assert.True(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Does_Not_Flag_ConcurrencyConflict_For_Plain_Business_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "An asset with this number already exists." }));
        var service = new AssetService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(),
            new AssetEditModel { AssetNumber = "AST-001", CategoryId = Guid.NewGuid(), Name = "MacBook Pro" }, expectedVersion: 2);

        Assert.False(result.Success);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new AssetService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(),
            new AssetEditModel { AssetNumber = "AST-001", CategoryId = Guid.NewGuid(), Name = "MacBook Pro" }, expectedVersion: 2);

        Assert.False(result.Success);
        Assert.Equal("Your session has expired. Please sign in again.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new AssetService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(),
            new AssetEditModel { AssetNumber = "AST-001", CategoryId = Guid.NewGuid(), Name = "MacBook Pro" }, expectedVersion: 2);

        Assert.False(result.Success);
        Assert.Equal("You do not have permission to perform this action.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Controlled_Failure_When_Success_Body_Is_Malformed()
    {
        var factory = BuildFactory(new MalformedJsonHandler());
        var service = new AssetService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(),
            new AssetEditModel { AssetNumber = "AST-001", CategoryId = Guid.NewGuid(), Name = "MacBook Pro" }, expectedVersion: 2);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ── RetireAssetAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RetireAssetAsync_Returns_Null_When_Api_Returns_NoContent()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NoContent, null));
        var service = new AssetService(factory);

        var error = await service.RetireAssetAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(error);
    }

    [Fact]
    public async Task RetireAssetAsync_Returns_Error_When_Api_Returns_NotFound()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NotFound, new { error = "Asset not found." }));
        var service = new AssetService(factory);

        var error = await service.RetireAssetAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal("Asset not found.", error);
    }

    // ── GetAssetAsync (representative read) ──────────────────────────────────────

    [Fact]
    public async Task GetAssetAsync_Returns_Value_When_Api_Returns_Ok()
    {
        var response = new AssetDetailModel(Guid.NewGuid(), Guid.NewGuid(), "AST-001", Guid.NewGuid(), "MacBook Pro", null, null, null, null, null, "Available", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new AssetService(factory);

        var result = await service.GetAssetAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetAssetAsync_Returns_Null_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new AssetService(factory);

        var result = await service.GetAssetAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    // ── Cancellation ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAssetAsync_Propagates_Cancellation_When_Token_Already_Cancelled()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, null));
        var service = new AssetService(factory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetAssetAsync(Guid.NewGuid(), Guid.NewGuid(), cts.Token));
    }
}
