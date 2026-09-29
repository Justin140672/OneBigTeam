using System.Net;
using System.Text;
using HR.SharedKernel.Http;

namespace HR.SharedKernel.Tests;

public class ApiResponseReaderTests
{
    private sealed record Sample(string Name);

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage NoBodyResponse(HttpStatusCode status) => new(status);


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


    private sealed class BrokenBodyContent(Exception toThrow) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.FromException(toThrow);

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromException<Stream>(toThrow);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ReadJsonAsync_Returns_Network_When_Body_Stream_Breaks_Mid_Read(HttpStatusCode status)
    {
        var content = new BrokenBodyContent(new IOException("The response ended prematurely."));
        content.Headers.ContentType = new("application/json");
        using var response = new HttpResponseMessage(status) { Content = content };

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.Network, result.FailureKind);
    }

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

public class ApiResultDisplayMessageTests
{
    private sealed record Sample(string Name);
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

    [Fact]
    public async Task ReadJsonAsync_Returns_InvalidResponse_For_200_With_NonJson_ContentType()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>proxy error</html>", Encoding.UTF8, "text/html"),
        };

        var result = await ApiResponseReader.ReadJsonAsync<Sample>(response);

        Assert.False(result.Success);
        Assert.Equal(ApiFailureKind.InvalidResponse, result.FailureKind);
    }

    [Theory]
    [InlineData(ApiFailureKind.Network, true)]
    [InlineData(ApiFailureKind.Server, true)]
    [InlineData(ApiFailureKind.InvalidResponse, true)]
    [InlineData(ApiFailureKind.Unauthenticated, false)]
    [InlineData(ApiFailureKind.Forbidden, false)]
    [InlineData(ApiFailureKind.NotFound, false)]
    [InlineData(ApiFailureKind.Validation, false)]
    [InlineData(ApiFailureKind.Conflict, false)]
    [InlineData(ApiFailureKind.Concurrency, false)]
    public void IsRetryable_Is_True_Only_For_Transient_Failures(ApiFailureKind kind, bool expected) =>
        Assert.Equal(expected, ApiResult<string>.Fail(kind, "x").IsRetryable);

    [Fact]
    public void Map_Projects_Success_And_Preserves_Failure_Details()
    {
        var ok = ApiResult<int>.Ok(2).Map(v => v.ToString());
        var failed = ApiResult<int>.Fail(ApiFailureKind.Conflict, "dupe", "code-1").Map(v => v.ToString());

        Assert.Equal("2", ok.Value);
        Assert.False(failed.Success);
        Assert.Equal(ApiFailureKind.Conflict, failed.FailureKind);
        Assert.Equal("dupe", failed.Error);
        Assert.Equal("code-1", failed.Code);
    }

    // ---- ExecuteFileAsync ----

    private static HttpResponseMessage FileResponse(byte[] bytes, string? dispositionFileName)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        if (dispositionFileName is not null)
            response.Content.Headers.ContentDisposition =
                new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = dispositionFileName };
        return response;
    }

    [Fact]
    public async Task ExecuteFileAsync_Returns_Bytes_And_FileName_From_ContentDisposition()
    {
        var result = await ApiResponseReader.ExecuteFileAsync(
            _ => Task.FromResult(FileResponse([1, 2, 3], "\"report.csv\"")), "fallback.csv");

        Assert.True(result.Success);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Value!.Bytes);
        Assert.Equal("report.csv", result.Value.FileName);
    }

    [Fact]
    public async Task ExecuteFileAsync_Uses_Fallback_And_Strips_Paths_From_Server_Supplied_Names()
    {
        var noHeader = await ApiResponseReader.ExecuteFileAsync(_ => Task.FromResult(FileResponse([1], null)), "fallback.csv");
        var traversal = await ApiResponseReader.ExecuteFileAsync(_ => Task.FromResult(FileResponse([1], "../../evil.csv")), "fallback.csv");

        Assert.Equal("fallback.csv", noHeader.Value!.FileName);
        Assert.Equal("evil.csv", traversal.Value!.FileName);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ApiFailureKind.Unauthenticated)]
    [InlineData(HttpStatusCode.Forbidden, ApiFailureKind.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ApiFailureKind.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError, ApiFailureKind.Server)]
    public async Task ExecuteFileAsync_Classifies_Failure_Statuses(HttpStatusCode status, ApiFailureKind expected)
    {
        var result = await ApiResponseReader.ExecuteFileAsync(
            _ => Task.FromResult(new HttpResponseMessage(status)), "fallback.csv");

        Assert.False(result.Success);
        Assert.Equal(expected, result.FailureKind);
    }

    [Fact]
    public async Task ExecuteFileAsync_Network_Failure_And_Timeout_Are_Network_Failures()
    {
        var network = await ApiResponseReader.ExecuteFileAsync(
            _ => throw new HttpRequestException("down"), "f.csv");
        var timeout = await ApiResponseReader.ExecuteFileAsync(
            _ => throw new TaskCanceledException("timeout"), "f.csv");

        Assert.Equal(ApiFailureKind.Network, network.FailureKind);
        Assert.Equal(ApiFailureKind.Network, timeout.FailureKind);
    }

    [Fact]
    public async Task ExecuteFileAsync_Propagates_Caller_Cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ApiResponseReader.ExecuteFileAsync(
                ct => { ct.ThrowIfCancellationRequested(); return Task.FromResult(FileResponse([1], null)); },
                "f.csv", cts.Token));
    }

    // ---- LogFailure ----

    private sealed class ListLogger : Microsoft.Extensions.Logging.ILogger
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    [Theory]
    [InlineData(ApiFailureKind.Network, true)]
    [InlineData(ApiFailureKind.Server, true)]
    [InlineData(ApiFailureKind.InvalidResponse, true)]
    [InlineData(ApiFailureKind.Unauthenticated, false)]
    [InlineData(ApiFailureKind.Forbidden, false)]
    [InlineData(ApiFailureKind.NotFound, false)]
    [InlineData(ApiFailureKind.Validation, false)]
    [InlineData(ApiFailureKind.Conflict, false)]
    public void LogFailure_Logs_Only_Operational_Failures_And_Never_The_Error_Text(ApiFailureKind kind, bool logged)
    {
        var logger = new ListLogger();
        var result = ApiResult<string>.Fail(kind, "response-body-secret");

        var returned = result.LogFailure(logger, "Op.Name");

        Assert.Same(result, returned);
        Assert.Equal(logged ? 1 : 0, logger.Messages.Count);
        Assert.All(logger.Messages, m =>
        {
            Assert.Contains("Op.Name", m);
            Assert.DoesNotContain("response-body-secret", m);
        });
    }

    [Fact]
    public void LogFailure_Does_Not_Log_Successes_Or_When_No_Logger()
    {
        var logger = new ListLogger();

        ApiResult<string>.Ok("x").LogFailure(logger, "Op");
        ApiResult<string>.Fail(ApiFailureKind.Server, "x").LogFailure(null, "Op");

        Assert.Empty(logger.Messages);
    }
}
