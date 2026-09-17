using System.Net;
using HR.Web.Models;
using HR.Web.Services;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class ProbationServiceTests
{
    // ── GetProbationRecordByEmployeeAsync — 404 is a legitimate "no record" outcome ──

    [Fact]
    public async Task GetProbationRecordByEmployeeAsync_Returns_Value_When_Api_Returns_Ok()
    {
        var response = new ProbationRecordModel(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today).AddMonths(6), "InProgress", null, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new ProbationService(factory);

        var result = await service.GetProbationRecordByEmployeeAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetProbationRecordByEmployeeAsync_Returns_Null_Without_Throwing_When_Api_Returns_NotFound()
    {
        // A missing probation record is expected/legitimate here — 404 must not be treated as a
        // hard failure, and no exception should propagate.
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NotFound, new { error = "No probation record." }));
        var service = new ProbationService(factory);

        var result = await service.GetProbationRecordByEmployeeAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetProbationRecordByEmployeeAsync_Returns_Null_When_Api_Returns_Unauthorized()
    {
        // Distinct failure kind from NotFound — still surfaces as null here (this method has no
        // separate error channel), but must not be conflated with a "confirmed no record" 404 by
        // the caller reading logs/behaviour; verified via a distinct FailureKind at the reader level.
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new ProbationService(factory);

        var result = await service.GetProbationRecordByEmployeeAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetProbationRecordByEmployeeAsync_Propagates_Cancellation_When_Token_Already_Cancelled()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, null));
        var service = new ProbationService(factory);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GetProbationRecordByEmployeeAsync(Guid.NewGuid(), Guid.NewGuid(), cts.Token));
    }

    // ── UpdateProbationRecordAsync(ApiSaveResult) ────────────────────────────────

    [Fact]
    public async Task UpdateProbationRecordAsync_Returns_Ok_When_Api_Returns_Success()
    {
        var response = new UpdateProbationRecordApiResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today).AddMonths(6), "InProgress", null, null, null, null, null, DateTimeOffset.UtcNow, 2);
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new ProbationService(factory);

        var request = new UpdateProbationRecordApiRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today).AddMonths(6), null, 1);
        var result = await service.UpdateProbationRecordAsync(Guid.NewGuid(), request);

        Assert.True(result.Success);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateProbationRecordAsync_Flags_ConcurrencyConflict_When_Api_Returns_Conflict_With_Concurrency_Code()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "Changed by someone else.", code = "concurrency" }));
        var service = new ProbationService(factory);

        var request = new UpdateProbationRecordApiRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today).AddMonths(6), null, 1);
        var result = await service.UpdateProbationRecordAsync(Guid.NewGuid(), request);

        Assert.False(result.Success);
        Assert.True(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateProbationRecordAsync_Does_Not_Flag_ConcurrencyConflict_For_Plain_Business_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "Cannot change manager while extension is pending." }));
        var service = new ProbationService(factory);

        var request = new UpdateProbationRecordApiRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today).AddMonths(6), null, 1);
        var result = await service.UpdateProbationRecordAsync(Guid.NewGuid(), request);

        Assert.False(result.Success);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task UpdateProbationRecordAsync_Returns_Failure_When_Api_Returns_Forbidden()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Forbidden, null));
        var service = new ProbationService(factory);

        var request = new UpdateProbationRecordApiRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today).AddMonths(6), null, 1);
        var result = await service.UpdateProbationRecordAsync(Guid.NewGuid(), request);

        Assert.False(result.Success);
        Assert.Equal("You do not have permission to perform this action.", result.ErrorMessage);
    }

    [Fact]
    public async Task UpdateProbationRecordAsync_Returns_Controlled_Failure_When_Success_Body_Is_Malformed()
    {
        var factory = BuildFactory(new MalformedJsonHandler());
        var service = new ProbationService(factory);

        var request = new UpdateProbationRecordApiRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateOnly.FromDateTime(DateTime.Today).AddMonths(6), null, 1);
        var result = await service.UpdateProbationRecordAsync(Guid.NewGuid(), request);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    // ── CompleteReviewAsync (idempotency-style guard indirectly exercised via API result) ──

    [Fact]
    public async Task CompleteReviewAsync_Returns_True_When_Api_Returns_Ok()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, null));
        var service = new ProbationService(factory);

        var result = await service.CompleteReviewAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

        Assert.True(result);
    }

    [Fact]
    public async Task CompleteReviewAsync_Returns_False_When_Api_Returns_Conflict_For_Already_Completed_Review()
    {
        // Guards against completing an already-completed review — the server rejects the repeat
        // call with 409, which must not be misreported as success.
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { error = "This review has already been completed." }));
        var service = new ProbationService(factory);

        var result = await service.CompleteReviewAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

        Assert.False(result);
    }

    [Fact]
    public async Task CompleteReviewAsync_Returns_False_When_Api_Returns_Unauthorized()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Unauthorized, null));
        var service = new ProbationService(factory);

        var result = await service.CompleteReviewAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null);

        Assert.False(result);
    }
}
