using System.Net;
using HR.Web.Models;
using HR.Web.Services;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class LeavePolicyServiceTests
{
    private static LeavePolicyEditModel SampleModel() => new() { Name = "Standard Policy" };

    // ── UpdateAsync(ApiSaveResult) ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_Returns_Ok_When_Api_Returns_Success()
    {
        var response = new UpdateLeavePolicyResponse(Guid.NewGuid(), Guid.NewGuid(), "Standard Policy", null, 5, false, true, false, DateTimeOffset.UtcNow, Version: 2);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new LeavePolicyService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.True(result.Success);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Flags_ConcurrencyConflict_When_Api_Returns_Conflict_With_Concurrency_Code()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "Changed by someone else.", code = "concurrency" }));
        var service = new LeavePolicyService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.True(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Does_Not_Flag_ConcurrencyConflict_For_Plain_Business_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "A policy with this name already exists." }));
        var service = new LeavePolicyService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new LeavePolicyService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.Equal("Your session has expired. Please sign in again.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new LeavePolicyService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.Equal("You do not have permission to perform this action.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Controlled_Failure_When_Success_Body_Is_Malformed()
    {
        var factory = BuildFactory(new MalformedJsonHandler());
        var service = new LeavePolicyService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ── GetLeavePolicyAsync (representative read) ────────────────────────────────

    [Fact]
    public async Task GetLeavePolicyAsync_Returns_Value_When_Api_Returns_Ok()
    {
        var response = new GetLeavePolicyResponse(Guid.NewGuid(), Guid.NewGuid(), "Standard Policy", null, 5, false, true, false, DateTimeOffset.UtcNow);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new LeavePolicyService(factory);

        var result = await service.GetLeavePolicyAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetLeavePolicyAsync_Returns_Null_When_Api_Returns_NotFound()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NotFound, new { error = "Leave policy not found." }));
        var service = new LeavePolicyService(factory);

        var result = await service.GetLeavePolicyAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    // ── SetDefaultLeavePolicyAsync ────────────────────────────────────────────────

    [Fact]
    public async Task SetDefaultLeavePolicyAsync_Returns_Null_When_Api_Returns_NoContent()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NoContent, null));
        var service = new LeavePolicyService(factory);

        var error = await service.SetDefaultLeavePolicyAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(error);
    }

    [Fact]
    public async Task SetDefaultLeavePolicyAsync_Returns_Error_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new LeavePolicyService(factory);

        var error = await service.SetDefaultLeavePolicyAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal("You do not have permission to perform this action.", error);
    }
}
