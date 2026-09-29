using HR.SharedKernel.Http;
using HR.Web.Services;

namespace HR.Web.Tests;

/// <summary>
/// View-model level coverage of how the UI presents failed API calls (no bUnit): messages are safe and
/// only transient failures offer a retry.
/// </summary>
public class ApiFailurePresenterTests
{
    private static ApiResult<int> Fail(ApiFailureKind kind, string? error = null) => ApiResult<int>.Fail(kind, error);

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
    public void Only_Transient_Failures_Are_Retryable(ApiFailureKind kind, bool retryable)
    {
        var presentation = ApiFailurePresenter.Present(Fail(kind, "x"), "save changes");

        Assert.Equal(retryable, presentation.IsRetryable);
        Assert.False(string.IsNullOrWhiteSpace(presentation.Message));
    }

    [Theory]
    [InlineData(ApiFailureKind.Network)]
    [InlineData(ApiFailureKind.Server)]
    [InlineData(ApiFailureKind.InvalidResponse)]
    [InlineData(ApiFailureKind.Unauthenticated)]
    [InlineData(ApiFailureKind.Forbidden)]
    [InlineData(ApiFailureKind.NotFound)]
    [InlineData(ApiFailureKind.Concurrency)]
    public void Messages_Never_Expose_Raw_Exception_Or_Server_Detail(ApiFailureKind kind)
    {
        var message = ApiFailurePresenter.Message(
            Fail(kind, "System.NullReferenceException at Handler.cs:line 42 connection string=secret"), "save changes");

        Assert.DoesNotContain("Exception", message);
        Assert.DoesNotContain("Handler.cs", message);
        Assert.DoesNotContain("secret", message);
    }

    [Fact]
    public void Validation_And_Conflict_Messages_Written_For_Users_Are_Passed_Through()
    {
        Assert.Equal("End date must be after the start date.",
            ApiFailurePresenter.Message(Fail(ApiFailureKind.Validation, "End date must be after the start date."), "save"));
        Assert.Equal("Already approved.",
            ApiFailurePresenter.Message(Fail(ApiFailureKind.Conflict, "Already approved."), "approve"));
    }

    [Fact]
    public void Validation_Errors_Dictionary_Is_Flattened()
    {
        var result = ApiResult<int>.Fail(
            ApiFailureKind.Validation, null, null,
            new Dictionary<string, string[]> { ["A"] = ["First."], ["B"] = ["Second."] });

        Assert.Equal("First. Second.", ApiFailurePresenter.Message(result, "save"));
    }

    [Fact]
    public void Forbidden_And_Unauthenticated_Are_Distinguishable()
    {
        var forbidden = ApiFailurePresenter.Message(Fail(ApiFailureKind.Forbidden), "approve this photo");
        var unauthenticated = ApiFailurePresenter.Message(Fail(ApiFailureKind.Unauthenticated), "approve this photo");

        Assert.NotEqual(forbidden, unauthenticated);
        Assert.Contains("permission", forbidden);
        Assert.Contains("sign in", unauthenticated);
    }

    [Fact]
    public void Retryable_Messages_Mention_Trying_Again()
    {
        Assert.Contains("try again", ApiFailurePresenter.Message(Fail(ApiFailureKind.Network), "load"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("try again", ApiFailurePresenter.Message(Fail(ApiFailureKind.Server), "load"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ErrorOrNull_Is_Null_On_Success_And_Message_On_Failure()
    {
        Assert.Null(ApiFailurePresenter.ErrorOrNull(ApiResult<int>.Ok(1), "load"));
        Assert.NotNull(ApiFailurePresenter.ErrorOrNull(Fail(ApiFailureKind.Server), "load"));
    }

    [Fact]
    public void Map_Carries_Failure_Through_And_Projects_Success()
    {
        var mapped = ApiResult<int>.Ok(3).Map(v => v * 2);
        var failed = Fail(ApiFailureKind.Forbidden, "no").Map(v => v * 2);

        Assert.Equal(6, mapped.Value);
        Assert.False(failed.Success);
        Assert.Equal(ApiFailureKind.Forbidden, failed.FailureKind);
    }
}
