using System.Net;
using HR.Infrastructure.Email;
using HR.Infrastructure.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HR.Infrastructure.Tests.Email;

/// <summary>
/// Defence-in-depth: the live Postmark senders must never dispatch to a permanently-undeliverable
/// (RFC 2606 reserved, or this app's *.example seed personas) address — a guaranteed hard bounce
/// that degrades the sending domain's reputation.
/// </summary>
public class PostmarkRecipientGuardTests
{
    [Theory]
    [InlineData("sarah.chen@acme.example")]
    [InlineData("grace.kim@betacorp.example")]
    [InlineData("e2e.seed07@acme.example")]
    [InlineData("ada@example.com")]
    [InlineData("ada@example.org")]
    [InlineData("ada@foo.test")]
    [InlineData("ada@thing.invalid")]
    [InlineData("ADA@ACME.EXAMPLE")]
    public void IsUndeliverable_True_For_Reserved_Domains(string email)
        => Assert.True(PostmarkRecipientGuard.IsUndeliverable(email));

    [Theory]
    [InlineData("ada@customer-mail.co")]
    [InlineData("someone@onebigteam.co.uk")]
    [InlineData("first.last@gmail.com")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("trailing@")]
    public void IsUndeliverable_False_For_Deliverable_Or_Malformed(string? email)
        => Assert.False(PostmarkRecipientGuard.IsUndeliverable(email));

    [Fact]
    public async Task PostmarkEmailSender_Skips_Send_For_Reserved_Domain_Without_Throwing()
    {
        var handler = new FakeHttpMessageHandler { StatusCodeToReturn = HttpStatusCode.OK, ResponseBodyToReturn = "{}" };
        var options = Options.Create(new PostmarkOptions { ServerToken = "tok", FromEmail = "no-reply@onebigteam.co.uk" });
        var sender = new PostmarkEmailSender(new HttpClient(handler), options, NullLogger<PostmarkEmailSender>.Instance);

        await sender.SendAsync("sarah.chen@acme.example", "Welcome", "<p>hi</p>");

        Assert.Empty(handler.Requests);
    }
}
