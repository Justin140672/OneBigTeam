using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using HR.Modules.Support.Domain;
using HR.Modules.Support.Features.AddSupportResponse;
using HR.Modules.Support.Features.SubmitSupportRequest;
using HR.Modules.Support.Jobs;
using HR.Modules.Support.Persistence;
using HR.Modules.Support.Services;
using HR.Modules.Support.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Modules.Support.Tests;

public class SupportEmailEncodingTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTimeOffset SeedNow = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);
    private const string BenignTitle = "Leave balance not updating";
    private const string LineSeparator = "\x2028";
    private const string ParagraphSeparator = "\x2029";

    public static TheoryData<string> HostileTitles => new()
    {
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "<svg/onload=alert(1)>",
        "<a href=\"javascript:alert(1)\">click</a>",
        "<a href='https://evil.example/'>Reset password</a>",
        "<p onclick=alert(1)>hi</p>",
        "\"><script>alert(1)</script>",
        "' onmouseover='alert(1)",
        "\" style=\"background:url(x)",
        "Tom & Sons <support@example.com>",
        "Already encoded &lt;b&gt;bold&lt;/b&gt; &amp; &quot;quoted&quot; &#60;script&#62;",
        "&lt;img src=x onerror=alert(1)&gt;",
        "--><script>alert(1)</script><!--",
        "</strong></p><h1>Injected</h1>",
        "Zoë's café — naïve résumé “smart quotes” 日本語 Привет 😀",
        "Line one\r\nBcc: attacker@example.com",
        "Tab\tand" + LineSeparator + "LS" + ParagraphSeparator + "PS",
    };


    [Theory]
    [MemberData(nameof(HostileTitles))]
    public async Task Submission_Admin_Alert_Renders_Title_As_Text_Only(string title)
    {
        var baseline = await SubmitAndCaptureAsync(BenignTitle);
        var sent = await SubmitAndCaptureAsync(title);

        AssertSameStructure(baseline.HtmlBody, sent.HtmlBody);
        AssertSubjectIsHeaderSafe(sent.Subject);

        var titleParagraph = Parse(sent.HtmlBody).QuerySelectorAll("p")
            .Single(p => p.TextContent.StartsWith("Title:", StringComparison.Ordinal));
        Assert.Equal("Title: " + title, titleParagraph.TextContent);
    }


    [Theory]
    [MemberData(nameof(HostileTitles))]
    public async Task Staff_Reply_Customer_Notification_Renders_Title_As_Text_Only(string title)
    {
        var baseline = await StaffReplyAndCaptureAsync(BenignTitle);
        var sent = await StaffReplyAndCaptureAsync(title);

        AssertSameStructure(baseline.HtmlBody, sent.HtmlBody);
        AssertSubjectIsHeaderSafe(sent.Subject);

        var firstParagraph = Parse(sent.HtmlBody).QuerySelectorAll("p").First();
        Assert.EndsWith($"\"{title}\".", firstParagraph.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Both_Paths_Keep_Unicode_And_Punctuation_Literal_In_The_Html()
    {
        const string title = "Zoë's café — “quoted” 日本語";

        var submitted = await SubmitAndCaptureAsync(title);
        var replied = await StaffReplyAndCaptureAsync(title);

        foreach (var body in new[] { submitted.HtmlBody, replied.HtmlBody })
        {
            Assert.Contains("Zoë", body, StringComparison.Ordinal);
            Assert.Contains("café — “quoted” 日本語", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Zoë's", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Retry_Notification_Is_Rendered_Through_The_Shared_Renderer()
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = SeedRequest(companyId, "<b>not markup</b>");
        var attempt = SupportNotificationAttempt.Create(
            Guid.NewGuid(), request.Id, companyId,
            SupportNotificationType.StaffReplyCustomerNotification, "customer@example.test", SeedNow);
        attempt.MarkFailed("Initial send failed.", SeedNow);
        db.SupportRequests.Add(request);
        db.SupportNotificationAttempts.Add(attempt);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        await new SupportNotificationRetryJob(db, emailSender, new FakeClock(FixedUtcNow)).ExecuteAsync();

        var sent = Assert.Single(emailSender.Sent);
        Assert.Equal(SupportEmailRenderer.RenderRetryNotification(request.ReferenceNumber), new SupportEmail(sent.Subject, sent.HtmlBody));
        AssertSubjectIsHeaderSafe(sent.Subject);
    }


    [Theory]
    [InlineData("Subject\r\nBcc: attacker@example.com", "Subject Bcc: attacker@example.com")]
    [InlineData("A\rB\nC\0D\u0085E" + LineSeparator + "F" + ParagraphSeparator + "G\tH", "A B C D E F G H")]
    [InlineData("  lots   of \r\n\r\n  space  ", "lots of space")]
    [InlineData("Tom & Sons — “ok” 日本語", "Tom & Sons — “ok” 日本語")]
    [InlineData("", "")]
    public void SanitizeSubject_Strips_Control_Characters_And_Keeps_Readable_Text(string input, string expected)
    {
        var subject = SupportEmailRenderer.SanitizeSubject(input);

        Assert.Equal(expected, subject);
        AssertSubjectIsHeaderSafe(subject);
    }

    [Fact]
    public void SanitizeSubject_Bounds_Length_Without_Splitting_A_Surrogate_Pair()
    {
        var input = new string('a', SupportEmailRenderer.MaxSubjectLength - 1) + "😀" + new string('b', 50);

        var subject = SupportEmailRenderer.SanitizeSubject(input);

        Assert.True(subject.Length <= SupportEmailRenderer.MaxSubjectLength);
        Assert.False(char.IsHighSurrogate(subject[^1]));
        Assert.Equal(new string('a', SupportEmailRenderer.MaxSubjectLength - 1), subject);
    }

    [Theory]
    [MemberData(nameof(HostileTitles))]
    public void Both_Paths_Share_The_Same_Title_Encoding(string title)
    {
        var encoded = SupportEmailRenderer.Encode(title);

        var admin = SupportEmailRenderer.RenderNewRequestAdminAlert(
            "SUP-2026-000001", SupportRequestType.ReportProblem, SupportRequestPriority.High, title, null);
        var reply = SupportEmailRenderer.RenderStaffReplyCustomerNotification("SUP-2026-000001", title);

        Assert.Contains(encoded, admin.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(encoded, reply.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain('<', encoded);
        Assert.DoesNotContain('>', encoded);
        Assert.DoesNotContain('"', encoded);
        Assert.DoesNotContain('\'', encoded);
    }


    private static IDocument Parse(string html) => new HtmlParser().ParseDocument(html);

    private static void AssertSameStructure(string baselineHtml, string actualHtml)
    {
        static List<string> Signature(string html) =>
            Parse(html).All
                .Select(e => e.LocalName + "[" + string.Join(",",
                    e.Attributes.OrderBy(a => a.Name, StringComparer.Ordinal).Select(a => a.Name + "=" + a.Value)) + "]")
                .ToList();

        Assert.Equal(Signature(baselineHtml), Signature(actualHtml));

        var document = Parse(actualHtml);
        Assert.Empty(document.QuerySelectorAll("script, img, svg, a, h1 ~ h1, iframe, style"));
        Assert.DoesNotContain(document.All.SelectMany(e => e.Attributes),
            a => a.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertSubjectIsHeaderSafe(string subject)
    {
        Assert.DoesNotContain('\r', subject);
        Assert.DoesNotContain('\n', subject);
        Assert.DoesNotContain(subject, c => char.IsControl(c) || c is (char)0x2028 or (char)0x2029);
        Assert.True(subject.Length <= SupportEmailRenderer.MaxSubjectLength);
    }

    private static SupportDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<SupportDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static IServiceScopeFactory BuildScopeFactory() =>
        new ServiceCollection()
            .AddDbContext<SupportDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString("N")))
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

    private static SupportRequest SeedRequest(Guid companyId, string title) =>
        SupportRequest.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), null,
            SupportRequestType.AskQuestion, title, "Description", SupportRequestPriority.Low,
            "SUP-2026-000123", null, null, null, false, null, null, SeedNow);

    private static async Task<FakeEmailSender.SentEmail> SubmitAndCaptureAsync(string title)
    {
        await using var db = BuildContext();
        var emailSender = new FakeEmailSender();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Support:AdminNotificationEmail"] = "support-admin@example.test",
            })
            .Build();
        var handler = new SubmitSupportRequestHandler(
            db, new FakeClock(FixedUtcNow), new FakeSupportAttachmentStorageService(),
            new SupportAttachmentValidator(), new FakeUploadedFileScanner(),
            emailSender, configuration, TestExecutionContext.Accessor, BuildScopeFactory(),
            NullLogger<SubmitSupportRequestHandler>.Instance);

        var result = await handler.HandleAsync(
            new SubmitSupportRequestRequest
            {
                CompanyId = Guid.NewGuid(),
                Type = SupportRequestType.ReportProblem,
                Title = title,
                Description = "Description",
                Priority = SupportRequestPriority.Medium,
            },
            Guid.NewGuid(), null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        return Assert.Single(emailSender.Sent);
    }

    private static async Task<FakeEmailSender.SentEmail> StaffReplyAndCaptureAsync(string title)
    {
        await using var db = BuildContext();
        var companyId = Guid.NewGuid();
        var request = SeedRequest(companyId, title);
        db.SupportRequests.Add(request);
        await db.SaveChangesAsync();

        var emailSender = new FakeEmailSender();
        var handler = new AddSupportResponseHandler(
            db, new FakeClock(FixedUtcNow), new FakeSupportAttachmentStorageService(),
            new SupportAttachmentValidator(), new FakeUploadedFileScanner(),
            emailSender, new FakeUserEmailReader("customer@example.test"),
            TestExecutionContext.Accessor, BuildScopeFactory(), NullLogger<AddSupportResponseHandler>.Instance);

        var result = await handler.HandleAsync(
            new AddSupportResponseRequest { CompanyId = companyId, Id = request.Id, BodyHtml = "Staff reply" },
            Guid.NewGuid(), isStaffResponse: true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        return Assert.Single(emailSender.Sent);
    }
}
