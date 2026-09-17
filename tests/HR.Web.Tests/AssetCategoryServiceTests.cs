using System.Net;
using HR.Web.Models;
using HR.Web.Services;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class AssetCategoryServiceTests
{
    private static UpdateAssetCategoryRequest SampleRequest(Guid companyId, Guid id) =>
        new(companyId, id, "Laptops", "IT equipment", 3);

    // ── UpdateAsync(ApiSaveResult) ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_Returns_Ok_When_Api_Returns_Success()
    {
        var response = new UpdateAssetCategoryResponse(Guid.NewGuid(), Guid.NewGuid(), "Laptops", "IT equipment", true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Version: 4);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new AssetCategoryService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), new AssetCategoryEditModel { Name = "Laptops" }, expectedVersion: 3);

        Assert.True(result.Success);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Flags_ConcurrencyConflict_On_409_With_Concurrency_Code()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "Changed by someone else.", code = "concurrency" }));
        var service = new AssetCategoryService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), new AssetCategoryEditModel { Name = "Laptops" }, expectedVersion: 3);

        Assert.False(result.Success);
        Assert.True(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Does_Not_Flag_ConcurrencyConflict_For_Plain_Business_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "A category with this name already exists." }));
        var service = new AssetCategoryService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), new AssetCategoryEditModel { Name = "Laptops" }, expectedVersion: 3);

        Assert.False(result.Success);
        Assert.False(result.IsConcurrencyConflict);
        Assert.Equal("A category with this name already exists.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new AssetCategoryService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), new AssetCategoryEditModel { Name = "Laptops" }, expectedVersion: 3);

        Assert.False(result.Success);
        Assert.False(result.IsConcurrencyConflict);
        Assert.Equal("Your session has expired. Please sign in again.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new AssetCategoryService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), new AssetCategoryEditModel { Name = "Laptops" }, expectedVersion: 3);

        Assert.False(result.Success);
        Assert.Equal("You do not have permission to perform this action.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Controlled_Failure_When_Success_Body_Is_Malformed()
    {
        var factory = BuildFactory(new MalformedJsonHandler());
        var service = new AssetCategoryService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), new AssetCategoryEditModel { Name = "Laptops" }, expectedVersion: 3);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Returns_Result_When_Api_Returns_Created()
    {
        var response = new CreateAssetCategoryResponse(Guid.NewGuid(), Guid.NewGuid(), "Laptops", null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Created, response));
        var service = new AssetCategoryService(factory);

        var (created, error) = await service.CreateAsync(Guid.NewGuid(), new CreateAssetCategoryRequest(Guid.NewGuid(), "Laptops", null));

        Assert.NotNull(created);
        Assert.Null(error);
    }

    // ── DeactivateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateAsync_Returns_Null_When_Api_Returns_NoContent()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NoContent, null));
        var service = new AssetCategoryService(factory);

        var error = await service.DeactivateAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(error);
    }

    [Fact]
    public async Task DeactivateAsync_Returns_Error_When_Api_Returns_NotFound()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NotFound, new { error = "Asset category not found." }));
        var service = new AssetCategoryService(factory);

        var error = await service.DeactivateAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal("Asset category not found.", error);
    }

    // ── ListAssetCategoriesAsync (representative read) ───────────────────────────

    [Fact]
    public async Task ListAssetCategoriesAsync_Returns_Items_When_Api_Returns_Ok()
    {
        // The API returns a raw JSON array for this endpoint (wrapped into ListAssetCategoriesResponse
        // client-side), not a { Items: [...] } envelope.
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, Array.Empty<object>()));
        var service = new AssetCategoryService(factory);

        var result = await service.ListAssetCategoriesAsync(Guid.NewGuid());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ListAssetCategoriesAsync_Returns_Null_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new AssetCategoryService(factory);

        var result = await service.ListAssetCategoriesAsync(Guid.NewGuid());

        Assert.Null(result);
    }
}
