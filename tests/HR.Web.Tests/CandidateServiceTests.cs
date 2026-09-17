using System.Net;
using HR.Web.Models;
using HR.Web.Services;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class CandidateServiceTests
{
    private static CandidateEditModel SampleModel() => new()
    {
        FirstName = "Jane",
        LastName = "Doe",
        Email = "jane@example.com",
    };

    // ── UpdateAsync(ApiSaveResult) — ANY 409 is a save conflict (no "code" needed) ──

    [Fact]
    public async Task UpdateAsync_Returns_Ok_When_Api_Returns_Success()
    {
        var response = new UpdateCandidateResponse(Guid.NewGuid(), Guid.NewGuid(), "Jane", "Doe", "jane@example.com", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Version: 2);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CandidateService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.True(result.Success);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Flags_ConcurrencyConflict_For_Any_409_With_No_Code()
    {
        // The recruitment API returns no "code" on its 409 body — ANY 409 must still be treated
        // as a save conflict, unlike the code-gated services.
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "Someone else changed this candidate." }));
        var service = new CandidateService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.True(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Flags_ConcurrencyConflict_For_409_With_Concurrency_Code_Too()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "Stale.", code = "concurrency" }));
        var service = new CandidateService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.True(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new CandidateService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.False(result.IsConcurrencyConflict);
        Assert.Equal("Your session has expired. Please sign in again.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Failure_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new CandidateService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.Equal("You do not have permission to perform this action.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateAsync_Returns_Controlled_Failure_When_Success_Body_Is_Malformed()
    {
        var factory = BuildFactory(new MalformedJsonHandler());
        var service = new CandidateService(factory);

        var result = await service.UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), SampleModel(), expectedVersion: 1);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ── CreateCandidateAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateCandidateAsync_Returns_Result_When_Api_Returns_Created()
    {
        var response = new CreateCandidateResponse(Guid.NewGuid(), Guid.NewGuid(), "Jane", "Doe", "jane@example.com", null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Created, response));
        var service = new CandidateService(factory);

        var (created, error) = await service.CreateCandidateAsync(Guid.NewGuid(), new CreateCandidateRequest(Guid.NewGuid(), "Jane", "Doe", "jane@example.com", null, null));

        Assert.NotNull(created);
        Assert.Null(error);
    }

    [Fact]
    public async Task CreateCandidateAsync_Returns_ValidationMessage_When_Api_Returns_BadRequest()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.BadRequest, new { error = "'not-an-email' is not a valid email address." }));
        var service = new CandidateService(factory);

        var (created, error) = await service.CreateCandidateAsync(Guid.NewGuid(), new CreateCandidateRequest(Guid.NewGuid(), "Jane", "Doe", "not-an-email", null, null));

        Assert.Null(created);
        Assert.Equal("'not-an-email' is not a valid email address.", error);
    }

    // ── DeactivateCandidateAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task DeactivateCandidateAsync_Returns_Failure_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new CandidateService(factory);

        var (result, error) = await service.DeactivateCandidateAsync(Guid.NewGuid(), Guid.NewGuid(), "No longer needed.");

        Assert.Null(result);
        Assert.Equal("Your session has expired. Please sign in again.", error);
    }

    [Fact]
    public async Task DeactivateCandidateAsync_Returns_Failure_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new CandidateService(factory);

        var (result, error) = await service.DeactivateCandidateAsync(Guid.NewGuid(), Guid.NewGuid(), "No longer needed.");

        Assert.Null(result);
        Assert.Equal("You do not have permission to perform this action.", error);
    }

    // ── GetCandidateAsync (representative read) ──────────────────────────────────

    [Fact]
    public async Task GetCandidateAsync_Returns_Value_When_Api_Returns_Ok()
    {
        var response = new GetCandidateResponse(Guid.NewGuid(), Guid.NewGuid(), "Jane", "Doe", "jane@example.com", null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CandidateService(factory);

        var result = await service.GetCandidateAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetCandidateAsync_Returns_Null_When_Api_Returns_NotFound()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NotFound, new { error = "Candidate not found." }));
        var service = new CandidateService(factory);

        var result = await service.GetCandidateAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }
}
