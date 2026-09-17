using System.Net;
using HR.Web.Models;
using HR.Web.Services;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class VacancyServiceTests
{
    private static VacancyEditModel SampleModel() => new()
    {
        PositionProfileId = Guid.NewGuid(),
        HiringManagerId = Guid.NewGuid(),
        AdvertTitle = "Software Engineer",
    };

    // ── UpdateAsync(ApiSaveResult) — ANY 409 is a save conflict (no "code" needed) ──

    [Fact]
    public async Task UpdateAsync_Returns_Ok_When_Api_Returns_Success()
    {
        var response = new UpdateVacancyResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Software Engineer", null, "Open", Guid.NewGuid(), null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Version: 2);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new VacancyService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.True(result.Success);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Flags_ConcurrencyConflict_For_Any_409_With_No_Code()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "Someone else changed this vacancy." }));
        var service = new VacancyService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.True(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Flags_ConcurrencyConflict_For_409_With_Concurrency_Code_Too()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "Stale.", code = "concurrency" }));
        var service = new VacancyService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.True(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new VacancyService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.Equal("Your session has expired. Please sign in again.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new VacancyService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.Equal("You do not have permission to perform this action.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Controlled_Failure_When_Success_Body_Is_Malformed()
    {
        var factory = BuildFactory(new MalformedJsonHandler());
        var service = new VacancyService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ── CreateVacancyAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateVacancyAsync_Returns_ValidationMessage_When_Api_Returns_BadRequest()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.BadRequest, new { error = "A hiring manager is required." }));
        var service = new VacancyService(factory);

        var (result, error) = await service.CreateVacancyAsync(Guid.NewGuid(),
            new CreateVacancyRequest(Guid.NewGuid(), Guid.NewGuid(), "Software Engineer", null, Guid.NewGuid()));

        Assert.Null(result);
        Assert.Equal("A hiring manager is required.", error);
    }

    // ── GetVacancyAsync (representative read) ────────────────────────────────────

    [Fact]
    public async Task GetVacancyAsync_Returns_Value_When_Api_Returns_Ok()
    {
        var response = new GetVacancyResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Software Engineer", null, "Open", Guid.NewGuid(), null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null, null, "Software Engineer", null, 0, true);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new VacancyService(factory);

        var result = await service.GetVacancyAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetVacancyAsync_Returns_Null_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new VacancyService(factory);

        var result = await service.GetVacancyAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }
}
