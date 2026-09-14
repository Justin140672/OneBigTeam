using HR.SharedKernel.Idempotency;

namespace HR.SharedKernel.Tests;

/// <summary>
/// Ticket 3 (P1) follow-up item 5: proves the sensitive-response guard actually fires for known
/// sensitive shapes, and stays quiet for an ordinary response DTO.
/// </summary>
public class ReplayResponsePolicyTests
{
    private sealed record SafeResponse(Guid Id, string Name, decimal Amount, DateTimeOffset CreatedAt);

    private sealed record ResponseWithAccessToken(Guid Id, string AccessToken);

    private sealed record ResponseWithPassword(Guid Id, string TemporaryPassword);

    private sealed record ResponseWithNestedSecret(Guid Id, NestedCredentials Credentials);

    private sealed record NestedCredentials(string ClientSecret);

    private sealed record ResponseWithSignedUrl(Guid Id, string SignedDocumentUrl);

    [Fact]
    public void Ordinary_Response_Passes()
    {
        ReplayResponsePolicy.EnsureReplaySafe<SafeResponse>();
    }

    [Fact]
    public void Response_With_AccessToken_Throws()
    {
        Assert.Throws<InvalidOperationException>(ReplayResponsePolicy.EnsureReplaySafe<ResponseWithAccessToken>);
    }

    [Fact]
    public void Response_With_Password_Throws()
    {
        Assert.Throws<InvalidOperationException>(ReplayResponsePolicy.EnsureReplaySafe<ResponseWithPassword>);
    }

    [Fact]
    public void Response_With_Nested_Secret_Throws()
    {
        Assert.Throws<InvalidOperationException>(ReplayResponsePolicy.EnsureReplaySafe<ResponseWithNestedSecret>);
    }

    [Fact]
    public void Response_With_Signed_Url_Throws()
    {
        Assert.Throws<InvalidOperationException>(ReplayResponsePolicy.EnsureReplaySafe<ResponseWithSignedUrl>);
    }
}
