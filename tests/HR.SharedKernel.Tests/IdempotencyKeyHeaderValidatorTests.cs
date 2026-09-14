using FastEndpoints;
using FluentValidation.Results;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;

namespace HR.SharedKernel.Tests;

/// <summary>
/// Ticket 3 (P1) follow-up item 4: the shared "Idempotency-Key" header validator, applied globally
/// as a FastEndpoints pre-processor. Exercised directly here (rather than through a full endpoint)
/// since <see cref="IPreProcessorContext"/> is a small enough surface to fake cleanly.
/// </summary>
public class IdempotencyKeyHeaderValidatorTests
{
    private static async Task<List<ValidationFailure>> ValidateAsync(string? headerValue, params string[] extraValues)
    {
        var httpContext = new DefaultHttpContext();
        if (headerValue is not null)
        {
            var all = new[] { headerValue }.Concat(extraValues).ToArray();
            httpContext.Request.Headers[IdempotencyKeyHeaderValidator.HeaderName] = all;
        }

        var context = new FakeContext(httpContext);
        await new IdempotencyKeyHeaderValidator().PreProcessAsync(context, CancellationToken.None);
        return context.ValidationFailures;
    }

    [Fact]
    public async Task Header_Absent_Passes()
    {
        var failures = await ValidateAsync(headerValue: null);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Valid_Key_Passes()
    {
        var failures = await ValidateAsync(Guid.NewGuid().ToString());
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Blank_Key_Fails()
    {
        var failures = await ValidateAsync("   ");
        Assert.NotEmpty(failures);
    }

    [Fact]
    public async Task Multiple_Header_Values_Fail_Rather_Than_Being_Joined()
    {
        var failures = await ValidateAsync("key-one", "key-two");
        Assert.NotEmpty(failures);
        Assert.Contains(failures, f => f.ErrorMessage.Contains("Multiple", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Too_Long_Key_Fails()
    {
        var failures = await ValidateAsync(new string('a', 201));
        Assert.NotEmpty(failures);
    }

    [Fact]
    public async Task Key_At_Length_Limit_Passes()
    {
        var failures = await ValidateAsync(new string('a', 200));
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Key_With_Control_Character_Fails()
    {
        var failures = await ValidateAsync("abc\ndef");
        Assert.NotEmpty(failures);
    }

    private sealed class FakeContext(HttpContext httpContext) : IPreProcessorContext
    {
        public object Request => new();
        public HttpContext HttpContext => httpContext;
        public List<ValidationFailure> ValidationFailures { get; } = [];
        public bool HasValidationFailures => ValidationFailures.Count > 0;
    }
}
