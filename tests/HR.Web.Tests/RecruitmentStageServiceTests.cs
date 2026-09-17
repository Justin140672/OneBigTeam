using System.Net;
using HR.Web.Models;
using HR.Web.Services;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class RecruitmentStageServiceTests
{
    private static RecruitmentStageEditModel SampleModel() => new() { Name = "Interview" };

    // ── UpdateAsync(ApiSaveResult) — ANY 409 is a save conflict (no "code" needed) ──

    [Fact]
    public async Task UpdateAsync_Returns_Ok_When_Api_Returns_Success()
    {
        var response = new UpdateRecruitmentStageResponse(Guid.NewGuid(), Guid.NewGuid(), "Interview", 1, true, false, RecruitmentStageTerminalOutcome.None, DateTimeOffset.UtcNow, Version: 2);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response, HrApiJsonOptions.Default));
        var service = new RecruitmentStageService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.True(result.Success);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Flags_ConcurrencyConflict_For_Any_409_With_No_Code()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "Someone else changed this stage." }));
        var service = new RecruitmentStageService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.True(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Flags_ConcurrencyConflict_For_409_With_Concurrency_Code_Too()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "Stale.", code = "concurrency" }));
        var service = new RecruitmentStageService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.True(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new RecruitmentStageService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.Equal("Your session has expired. Please sign in again.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new RecruitmentStageService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.Equal("You do not have permission to perform this action.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Controlled_Failure_When_Success_Body_Is_Malformed()
    {
        var factory = BuildFactory(new MalformedJsonHandler());
        var service = new RecruitmentStageService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_Returns_Failure_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new RecruitmentStageService(factory);

        var (result, error) = await service.CreateAsync(Guid.NewGuid(),
            new CreateRecruitmentStageRequest(Guid.NewGuid(), "Interview", 1, false, RecruitmentStageTerminalOutcome.None));

        Assert.Null(result);
        Assert.Equal("Your session has expired. Please sign in again.", error);
    }

    // ── ListStagesAsync (representative read) ────────────────────────────────────

    [Fact]
    public async Task ListStagesAsync_Returns_Value_When_Api_Returns_Ok()
    {
        var response = new ListRecruitmentStagesResponse([]);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new RecruitmentStageService(factory);

        var result = await service.ListStagesAsync(Guid.NewGuid());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ListStagesAsync_Returns_Null_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new RecruitmentStageService(factory);

        var result = await service.ListStagesAsync(Guid.NewGuid());

        Assert.Null(result);
    }
}
