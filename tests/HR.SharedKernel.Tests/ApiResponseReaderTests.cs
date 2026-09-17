using System.Net;
using System.Text;
using HR.SharedKernel.Http;

namespace HR.SharedKernel.Tests;

// Pins the shared HTTP response-reading layer's status-code -> ApiFailureKind classification and
// the { error, code } / { errors } envelope parsing it replaces ~90 files' worth of bespoke local
// parsing for. In particular: a 409 is Concurrency only when code == "concurrency" (the existing
// EditSectionBase/SaveConflictBanner convention), never inferred any other way.
public class ApiResponseReaderTests
{
    private sealed record Sample(string Name);

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage NoBodyResponse(HttpStatusCode status) => new(status);

    // ── ReadJsonAsync: success ──────────────────────────────────────────────────

    [Fact]
    public async Task ReadJsonAsync_Returns_Success_With_Value_For_200_With_Valid_Body()
    {
        var response = JsonResponse(HttpStatusCode.OK, """{"name":"Alice"}""");

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.True(result.Success);
        Assert.Equal(ApiFailureKind.None, result.FailureKind);
        Assert.Equal("Alice", result.Value!.Name);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_InvalidResponse_For_200_With_Malformed_Body()
    {
        var response = JsonResponse(HttpStatusCode.OK, "not json at all {{{");

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.InvalidResponse, result.FailureKind);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_InvalidResponse_For_200_With_Empty_Body()
    {
        var response = JsonResponse(HttpStatusCode.OK, "");

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.InvalidResponse, result.FailureKind);
    }

    // ── ReadNoContentAsync: success ─────────────────────────────────────────────

    [Fact]
    public async Task ReadNoContentAsync_Returns_Success_For_204_No_Body()
    {
        var response = NoBodyResponse(HttpStatusCode.NoContent);

        var result = await ApiResponseReader.ReadNoContentAsync(response);

        Assert.True(result.Success);
        Assert.Equal(ApiFailureKind.None, result.FailureKind);
    }

    [Fact]
    public async Task ReadNoContentAsync_Returns_Success_For_200_No_Body()
    {
        var response = NoBodyResponse(HttpStatusCode.OK);

        var result = await ApiResponseReader.ReadNoContentAsync(response);

        Assert.True(result.Success);
    }

    // ── 401 / 403 / 404 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadJsonAsync_Returns_Unauthenticated_For_401()
    {
        var response = NoBodyResponse(HttpStatusCode.Unauthorized);

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Unauthenticated, result.FailureKind);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_Forbidden_For_403()
    {
        var response = NoBodyResponse(HttpStatusCode.Forbidden);

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Forbidden, result.FailureKind);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_NotFound_For_404_With_No_Body()
    {
        var response = NoBodyResponse(HttpStatusCode.NotFound);

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.NotFound, result.FailureKind);
        Assert.Equal("The requested item could not be found.", result.Error);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_NotFound_For_404_With_Error_Envelope()
    {
        var response = JsonResponse(HttpStatusCode.NotFound, """{"error":"Employee not found."}""");

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.NotFound, result.FailureKind);
        Assert.Equal("Employee not found.", result.Error);
    }

    // ── 409 concurrency vs plain conflict ───────────────────────────────────────

    [Fact]
    public async Task ReadJsonAsync_Returns_Concurrency_For_409_With_Concurrency_Code()
    {
        var response = JsonResponse(HttpStatusCode.Conflict, """{"error":"Changed by someone else.","code":"concurrency"}""");

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Concurrency, result.FailureKind);
        Assert.True(result.IsConcurrencyConflict);
        Assert.Equal("Changed by someone else.", result.Error);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_Conflict_For_409_With_No_Code()
    {
        var response = JsonResponse(HttpStatusCode.Conflict, """{"error":"A conflict occurred."}""");

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Conflict, result.FailureKind);
        Assert.False(result.IsConcurrencyConflict);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_Conflict_For_409_With_Different_Code()
    {
        var response = JsonResponse(HttpStatusCode.Conflict, """{"error":"Duplicate email.","code":"duplicate_email"}""");

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Conflict, result.FailureKind);
        Assert.False(result.IsConcurrencyConflict);
    }

    // ── 400 / 422 validation ─────────────────────────────────────────────────────

    [Fact]
    public async Task ReadJsonAsync_Returns_Validation_For_400_With_Validation_Envelope()
    {
        var response = JsonResponse(HttpStatusCode.BadRequest,
            """{"errors":{"FirstName":["First name is required.","First name is too long."]}}""");

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Validation, result.FailureKind);
        Assert.NotNull(result.ValidationErrors);
        Assert.Equal(["First name is required.", "First name is too long."], result.ValidationErrors!["FirstName"]);
        Assert.Equal("First name is required. First name is too long.", result.DisplayMessage);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_Validation_For_422_With_Validation_Envelope()
    {
        var response = JsonResponse(HttpStatusCode.UnprocessableEntity,
            """{"errors":{"PostCode":["Post code is required."]}}""");

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Validation, result.FailureKind);
        Assert.Equal("Post code is required.", result.DisplayMessage);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_Validation_For_400_With_Only_Error_Envelope()
    {
        var response = JsonResponse(HttpStatusCode.BadRequest, """{"error":"'12345' is not a valid mobile number."}""");

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Validation, result.FailureKind);
        Assert.Equal("'12345' is not a valid mobile number.", result.Error);
        Assert.Equal("'12345' is not a valid mobile number.", result.DisplayMessage);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_InvalidResponse_For_400_With_Unrecognised_Body_Shape()
    {
        var response = JsonResponse(HttpStatusCode.BadRequest, """{"somethingElse":123}""");

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.InvalidResponse, result.FailureKind);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_InvalidResponse_For_422_With_Unrecognised_Body_Shape()
    {
        var response = JsonResponse(HttpStatusCode.UnprocessableEntity, "not json at all");

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.InvalidResponse, result.FailureKind);
    }

    // ── 5xx and unmapped statuses ────────────────────────────────────────────────

    [Fact]
    public async Task ReadJsonAsync_Returns_Server_For_500()
    {
        var response = NoBodyResponse(HttpStatusCode.InternalServerError);

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Server, result.FailureKind);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_Server_For_503()
    {
        var response = NoBodyResponse(HttpStatusCode.ServiceUnavailable);

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Server, result.FailureKind);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_Server_For_Unmapped_Status_Code()
    {
        var response = NoBodyResponse((HttpStatusCode)418);

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Server, result.FailureKind);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ReadJsonAsync_Returns_Server_With_Fallback_Message_For_Malformed_Body_On_500()
    {
        var response = JsonResponse(HttpStatusCode.InternalServerError, "<html>not json</html>");

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Server, result.FailureKind);
        Assert.NotNull(result.Error);
    }

    // ── Cancellation propagation ─────────────────────────────────────────────────

    [Fact]
    public async Task ReadJsonAsync_Rethrows_When_Supplied_Token_Already_Cancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var response = JsonResponse(HttpStatusCode.OK, """{"name":"Alice"}""");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ApiResponseReader.ReadJsonAsync<Sample>(response, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ReadJsonAsync_Rethrows_When_Supplied_Token_Already_Cancelled_On_Failure_Path()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var response = JsonResponse(HttpStatusCode.NotFound, """{"error":"missing"}""");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ApiResponseReader.ReadJsonAsync<Sample>(response, cancellationToken: cts.Token));
    }

    // ── ExecuteAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Network_When_Send_Throws_HttpRequestException()
    {
        var result = await ApiResponseReader.ExecuteAsync<Sample>(
            _ => throw new HttpRequestException("boom"));

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Network, result.FailureKind);
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Network_When_Send_Throws_OperationCanceledException_Not_From_Supplied_Token()
    {
        // Simulates an internal HttpClient timeout: the exception is not caused by the caller's
        // own (never-cancelled) token, so it must be classified as a network failure, not rethrown.
        var neverCancelled = new CancellationTokenSource().Token;

        var result = await ApiResponseReader.ExecuteAsync<Sample>(
            _ => throw new OperationCanceledException("timed out"),
            cancellationToken: neverCancelled);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Network, result.FailureKind);
    }

    [Fact]
    public async Task ExecuteAsync_Rethrows_When_Send_Throws_OperationCanceledException_From_Supplied_Token()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ApiResponseReader.ExecuteAsync<Sample>(
                _ => throw new OperationCanceledException("cancelled"),
                cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ExecuteAsync_Delegates_To_ReadJsonAsync_On_Success()
    {
        var result = await ApiResponseReader.ExecuteAsync<Sample>(
            _ => Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"name":"Bob"}""")));

        Assert.True(result.Success);
        Assert.Equal("Bob", result.Value!.Name);
    }

    [Fact]
    public async Task ExecuteAsync_Delegates_To_ReadJsonAsync_On_Failure()
    {
        var result = await ApiResponseReader.ExecuteAsync<Sample>(
            _ => Task.FromResult(JsonResponse(HttpStatusCode.Conflict, """{"error":"stale","code":"concurrency"}""")));

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Concurrency, result.FailureKind);
    }
}

// ApiResult<T>.DisplayMessage: flattens ValidationErrors when present, falls back to Error.
public class ApiResultDisplayMessageTests
{
    [Fact]
    public void DisplayMessage_Flattens_ValidationErrors_When_Present()
    {
        var result = ApiResult<string>.Fail(
            ApiFailureKind.Validation,
            error: null,
            validationErrors: new Dictionary<string, string[]>
            {
                ["FirstName"] = ["First name is required."],
                ["LastName"] = ["Last name is required.", "Last name is too long."]
            });

        Assert.Equal(
            "First name is required. Last name is required. Last name is too long.",
            result.DisplayMessage);
    }

    [Fact]
    public void DisplayMessage_Falls_Back_To_Error_When_No_ValidationErrors()
    {
        var result = ApiResult<string>.Fail(ApiFailureKind.Conflict, "A conflict occurred.");

        Assert.Equal("A conflict occurred.", result.DisplayMessage);
    }

    [Fact]
    public void DisplayMessage_Falls_Back_To_Error_When_ValidationErrors_Is_Empty()
    {
        var result = ApiResult<string>.Fail(
            ApiFailureKind.Validation, "Validation failed.", validationErrors: new Dictionary<string, string[]>());

        Assert.Equal("Validation failed.", result.DisplayMessage);
    }

    [Fact]
    public void IsConcurrencyConflict_True_Only_For_Concurrency_FailureKind()
    {
        var concurrency = ApiResult<string>.Fail(ApiFailureKind.Concurrency, "stale", "concurrency");
        var conflict = ApiResult<string>.Fail(ApiFailureKind.Conflict, "dupe");

        Assert.True(concurrency.IsConcurrencyConflict);
        Assert.False(conflict.IsConcurrencyConflict);
    }
}
