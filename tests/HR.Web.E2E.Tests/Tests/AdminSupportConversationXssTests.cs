using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// P1 "Prevent stored XSS in administrator support conversations". A tenant user (support:manage)
/// posts a reply whose body mixes safe formatting with several script-execution attempts, each of
/// which would set a distinct <c>window.__xss</c> value if it ever ran. The reply is then viewed:
/// <list type="bullet">
/// <item>in HR.Web's own support thread (same-origin tenant view), and</item>
/// <item>in the Admin Portal's support request detail page — the cross-origin platform-admin sink
/// this ticket closes.</item>
/// </list>
/// Both must render the safe formatting and nothing executable. Seeding and personas follow
/// <see cref="AdminSupportRequestStatusConcurrencyTests"/>: Laura seeds via HR.Web's Help &amp;
/// Feedback page, priya.shah (allow-listed platform administrator) views it in the Admin Portal.
/// Each test uses a uniquely-titled request and unique body marker, so it only ever inspects its
/// own data (deterministic at maxParallelThreads=15).
/// </summary>
public sealed class AdminSupportConversationXssTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";
    private const string AllowListedAdminEmail = "priya.shah@acme.example";

    private const string SafeLinkHref = "https://example.com/help";

    private static string BuildPayload(string marker) =>
        $"<p>Safe <strong>bold</strong> <a href=\"{SafeLinkHref}\">help</a> {marker}</p>" +
        "<script>window.__xss=1</script>" +
        "<img src=x onerror=\"window.__xss=2\">" +
        "<svg onload=\"window.__xss=3\"></svg>" +
        "<iframe src=\"javascript:window.__xss=4\"></iframe>" +
        "<a href=\"JaVaScRiPt:window.__xss=5\">x</a>" +
        "<ScRiPt>window.__xss=6</sCrIpT>" +
        "<details open ontoggle=\"window.__xss=7\"><summary>s</summary></details>" +
        "<a href=\"&#x6A;avascript:window.__xss=8\">y</a>";

    // Elements that must never survive inside a rendered support body.
    private const string ForbiddenSelector =
        "script,iframe,object,embed,svg,img,math,style,link,meta,base,form,input,button,video,audio,template,noscript,details";

    /// <summary>Tag names of forbidden elements found inside any element matching <paramref name="bodySelector"/>.</summary>
    private static Task<string[]> FindForbiddenElementsAsync(IPage page, string bodySelector) =>
        page.EvaluateAsync<string[]>(
            @"([bodySelector, forbidden]) => Array.from(document.querySelectorAll(bodySelector))
                .flatMap(b => Array.from(b.querySelectorAll(forbidden)))
                .map(e => e.tagName.toLowerCase())",
            new object[] { bodySelector, ForbiddenSelector });

    /// <summary>"tag@attr" for every attribute whose name starts with "on", on the body containers or any descendant.</summary>
    private static Task<string[]> FindEventHandlerAttributesAsync(IPage page, string bodySelector) =>
        page.EvaluateAsync<string[]>(
            @"bodySelector => Array.from(document.querySelectorAll(bodySelector))
                .flatMap(b => [b, ...Array.from(b.querySelectorAll('*'))])
                .flatMap(e => Array.from(e.attributes)
                    .filter(a => a.name.toLowerCase().startsWith('on'))
                    .map(a => e.tagName.toLowerCase() + '@' + a.name))",
            bodySelector);

    /// <summary>Every href/src/action/formaction value in the bodies that resolves to a script-capable scheme.</summary>
    private static Task<string[]> FindDangerousUrlsAsync(IPage page, string bodySelector) =>
        page.EvaluateAsync<string[]>(
            @"bodySelector => Array.from(document.querySelectorAll(bodySelector))
                .flatMap(b => Array.from(b.querySelectorAll('[href],[src],[action],[formaction],[xlink\\:href]')))
                .flatMap(e => ['href', 'src', 'action', 'formaction', 'xlink:href']
                    .map(n => e.getAttribute(n))
                    .filter(v => v !== null))
                .filter(v => /^(javascript|data|vbscript):/i.test(v.replace(/[\u0000- ]/g, '')))",
            bodySelector);

    private static Task<bool> NoPayloadExecutedAsync(IPage page) =>
        page.EvaluateAsync<bool>("() => typeof window.__xss === 'undefined'");

    private static async Task AssertBodiesAreInertAsync(IPage page, string bodySelector, string view)
    {
        var forbidden = await FindForbiddenElementsAsync(page, bodySelector);
        Assert.True(forbidden.Length == 0,
            $"[{view}] Forbidden elements rendered inside support bodies: {string.Join(", ", forbidden)}");

        var handlers = await FindEventHandlerAttributesAsync(page, bodySelector);
        Assert.True(handlers.Length == 0,
            $"[{view}] Event-handler attributes rendered inside support bodies: {string.Join(", ", handlers)}");

        var urls = await FindDangerousUrlsAsync(page, bodySelector);
        Assert.True(urls.Length == 0,
            $"[{view}] Script-capable URLs rendered inside support bodies: {string.Join(", ", urls)}");

        Assert.True(await NoPayloadExecutedAsync(page),
            $"[{view}] window.__xss was set — an injected payload executed");
    }

    [Fact]
    public async Task MaliciousReply_RendersSafeFormatting_AndExecutesNothing_InHrWebAndAdminPortal()
    {
        var marker = $"xss-marker-{Guid.NewGuid().ToString("N")[..8]}";
        var requestId = await SeedSupportRequestAsync("E2E Admin Conversation XSS");

        // ── HR.Web: post the malicious reply through the real reply box ──
        var hrWebDetail = new SupportRequestDetailPage(_page, _fixture.WebBaseUrl);
        await hrWebDetail.FillReplyAsync(BuildPayload(marker));
        await hrWebDetail.SendReplyAsync();

        Assert.True(await hrWebDetail.HasThreadEntryAsync(marker),
            $"Expected the reply containing '{marker}' to appear in the HR.Web conversation thread");
        await AssertBodiesAreInertAsync(_page, ".support-thread-item .support-thread-body", "HR.Web");

        // ── Admin Portal: the cross-origin platform-admin view of the same conversation ──
        var adminLogin = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var adminDetail = new AdminSupportRequestDetailPage(_page, _fixture.AdminWebBaseUrl);

        await adminLogin.GoToAsync();
        await adminLogin.LoginAsync(AllowListedAdminEmail);

        await adminDetail.GoToAsync(AcmeId, requestId);
        Assert.False(await adminDetail.IsErrorBannerVisibleAsync(),
            "Expected the Admin Portal support request detail page to load, not the error banner");

        await adminDetail.WaitForConversationAsync(expectedCount: 1);
        var body = adminDetail.ConversationBodies.First;
        await Assertions.Expect(body).ToContainTextAsync(marker);

        await AssertBodiesAreInertAsync(_page, "div.support-response-body", "Admin Portal");

        // Safe formatting survives and the permitted link is hardened.
        await Assertions.Expect(body.Locator("strong", new() { HasText = "bold" })).ToHaveCountAsync(1);
        var safeLink = body.Locator($"a[href='{SafeLinkHref}'][rel*='noopener'][target='_blank']");
        await Assertions.Expect(safeLink).ToHaveCountAsync(1);
        await Assertions.Expect(safeLink).ToHaveTextAsync("help");

        // The javascript:-scheme links survive only as inert, href-less text.
        await Assertions.Expect(body.Locator("a:not([href])")).ToHaveCountAsync(2);

        // Give any deferred handler (e.g. a late-loading <img onerror>, <details ontoggle>) a
        // chance to fire before the final check, then re-assert nothing ran. A bounded settle is
        // used rather than NetworkIdle, which is not a reliable signal with a live Blazor Server
        // circuit; there is no positive event to wait for when asserting that nothing happened.
        await _page.WaitForTimeoutAsync(750);
        Assert.True(await NoPayloadExecutedAsync(_page),
            "window.__xss was set on the Admin Portal after the page settled — an injected payload executed");
    }

    /// <summary>
    /// Submits a uniquely-titled support request as Laura via HR.Web's Help &amp; Feedback page and
    /// leaves the browser on its detail page (the submit navigates there). Returns the new id.
    /// </summary>
    private async Task<Guid> SeedSupportRequestAsync(string label)
    {
        var title = $"{label} {Guid.NewGuid().ToString("N")[..8]}";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var help = new HelpFeedbackPage(_page, _fixture.WebBaseUrl);
        var detail = new SupportRequestDetailPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await help.GoToAsync(AcmeId);
        await help.SelectTypeAsync("Ask a Question");
        await help.FillTitleAsync(title);
        await help.FillDescriptionAsync("Created by E2E test — please ignore.");
        await help.SelectPriorityAsync("Low");
        await help.SubmitAsync();

        // Wait for this request's own detail page to mount (not a stale layout h1) before reading
        // the id from the URL and using its reply box.
        Assert.Equal(title, await detail.GetTitleAsync());
        return UrlIdParser.LastGuid(_page.Url);
    }
}
