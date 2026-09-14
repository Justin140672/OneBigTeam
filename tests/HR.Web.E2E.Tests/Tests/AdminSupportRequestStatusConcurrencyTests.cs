using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 16: optimistic-concurrency conflict/reload UI on HR.Admin.Web's Support Request status
/// editor (SupportRequestDetails.razor, /customers/{CompanyId}/support-requests/{Id}) — a bespoke
/// inline reimplementation of HR.Web's shared SaveConflictBanner contract (HR.Admin.Web cannot
/// reference HR.Web). On save, the loaded Version is submitted as ExpectedVersion; on success the
/// local Version is replaced with whatever the API returns; on HTTP 409 the page shows a conflict
/// banner ("Someone else changed this support request while you were viewing it...") with a
/// "Reload latest values" button that re-fetches the record and clears the conflict state without
/// overwriting the winning value.
///
/// TICKET 17 DESIGN NOTE: HR.Admin.Web no longer calls the tenant "support:manage" routes at all —
/// it now calls the dedicated platform-support admin surface
/// (/api/admin/companies/{companyId}/support/requests..., see
/// HR.Modules.Support.Features.*.AdminEndpoint.cs) which reuses the tenant handlers unchanged but
/// is gated purely by the "platform:admin" policy (PlatformAdminAuthorizationHandler — an enabled
/// PlatformAdministrator row matched by SupabaseAuthUserId or email). There is therefore no more
/// need to borrow a tenant HrAdministrator persona to satisfy "support:manage": any allow-listed,
/// enabled platform administrator can reach the Admin Portal's support pages regardless of what
/// tenant role (if any) they hold. "priya.shah@acme.example" — seeded purely as a
/// CompanyAdministrator on the Acme tenant and separately allow-listed as an enabled
/// PlatformAdministrator via "PlatformAdmin:AllowedEmails" in
/// src/HR.Api/appsettings.Development.json (see IdentityModule.SeedPlatformAdministratorsFromConfigAsync) —
/// is used directly below without any tenant-role impersonation workaround.
/// </summary>
public sealed class AdminSupportRequestStatusConcurrencyTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    // Staff persona used to submit the seed support request via HR.Web's Help & Feedback page — a
    // platform-admin-only persona (e.g. priya.shah) has no employee record and cannot log into
    // HR.Web itself, so a real tenant HR.Web login is still needed for seeding.
    private const string LauraEmail = "laura.bennett@acme.example";

    // Platform-admin-allow-listed persona used for the Admin Portal itself. Purely a
    // CompanyAdministrator on the tenant side — no "support:manage" grant needed any more, since
    // the Admin Portal now calls the "platform:admin"-gated admin routes exclusively.
    private const string AllowListedAdminEmail = "priya.shah@acme.example";

    [Fact]
    public async Task Queue_LoadsForSeededCompany_AndShowsSeededRequest()
    {
        var title = await SeedSupportRequestAsync("E2E Admin Queue Load");

        var adminLogin = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var queue = new AdminSupportRequestQueuePage(_page, _fixture.AdminWebBaseUrl);

        await adminLogin.GoToAsync();
        await adminLogin.LoginAsync(AllowListedAdminEmail);

        await queue.GoToAsync(AcmeId);

        Assert.False(await queue.IsErrorBannerVisibleAsync(),
            "Expected the support request queue to load, not the not-authorised error banner");
        Assert.True(await queue.HasRequestAsync(title),
            $"Expected the seeded support request '{title}' to appear in the Admin Portal queue");
    }

    [Fact]
    public async Task DirectLink_FromCustomerDetails_NavigatesToQueue()
    {
        var adminLogin = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var details = new CustomerDetailsPage(_page, _fixture.AdminWebBaseUrl);
        var queue = new AdminSupportRequestQueuePage(_page, _fixture.AdminWebBaseUrl);

        await adminLogin.GoToAsync();
        await adminLogin.LoginAsync(AllowListedAdminEmail);

        await details.GoToAsync(AcmeId);
        await details.OpenSupportRequestsLink.ClickAsync();

        await _page.WaitForURLAsync($"**/customers/{AcmeId}/support-requests", new()
        {
            Timeout = 20_000,
            WaitUntil = Microsoft.Playwright.WaitUntilState.Commit,
        });
        Assert.False(await queue.IsErrorBannerVisibleAsync(),
            "Expected the support request queue to load via the 'Open support requests' link");
    }

    [Fact]
    public async Task Detail_OpensFromQueue_AndShowsCurrentStatus()
    {
        var title = await SeedSupportRequestAsync("E2E Admin Detail Status");

        var adminLogin = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var queue = new AdminSupportRequestQueuePage(_page, _fixture.AdminWebBaseUrl);
        var detail = new AdminSupportRequestDetailPage(_page, _fixture.AdminWebBaseUrl);

        await adminLogin.GoToAsync();
        await adminLogin.LoginAsync(AllowListedAdminEmail);

        await queue.GoToAsync(AcmeId);
        await queue.OpenRequestAsync(title);

        Assert.False(await detail.IsErrorBannerVisibleAsync(),
            "Expected the support request detail page to load, not the not-authorised error banner");
        Assert.Equal(title, await detail.GetTitleAsync());

        // A freshly-submitted request starts life as SupportRequestStatus.Submitted (see
        // SupportRequest.cs's constructor).
        Assert.Equal("Submitted", await detail.GetSelectedStatusAsync());
    }

    [Fact]
    public async Task ChangeStatus_NoConflict_PersistsAfterReload()
    {
        var title = await SeedSupportRequestAsync("E2E Admin Status Change");

        var adminLogin = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var queue = new AdminSupportRequestQueuePage(_page, _fixture.AdminWebBaseUrl);
        var detail = new AdminSupportRequestDetailPage(_page, _fixture.AdminWebBaseUrl);

        await adminLogin.GoToAsync();
        await adminLogin.LoginAsync(AllowListedAdminEmail);

        await queue.GoToAsync(AcmeId);
        await queue.OpenRequestAsync(title);
        var id = UrlIdParser.LastGuid(_page.Url);

        await detail.SelectStatusAsync("UnderReview");
        await detail.SaveAsync();

        Assert.True(await detail.IsSuccessMessageVisibleAsync(),
            "Expected a success message after saving the new status with no conflicting change");
        Assert.False(await detail.IsConflictBannerVisibleAsync());

        // Reload the page entirely (not just re-navigate client-side) to confirm the new status
        // actually persisted server-side, not merely in local component state.
        await detail.GoToAsync(AcmeId, id);
        Assert.Equal("UnderReview", await detail.GetSelectedStatusAsync());
    }

    [Fact]
    public async Task ChangeStatus_AfterAnotherActorChangedIt_ShowsConflictBanner_ThenReloadRecovers()
    {
        var title = await SeedSupportRequestAsync("E2E Admin Status Conflict");

        var adminLogin = new AdminLoginPage(_page, _fixture.AdminWebBaseUrl);
        var queue = new AdminSupportRequestQueuePage(_page, _fixture.AdminWebBaseUrl);
        var detail = new AdminSupportRequestDetailPage(_page, _fixture.AdminWebBaseUrl);

        await adminLogin.GoToAsync();
        await adminLogin.LoginAsync(AllowListedAdminEmail);

        await queue.GoToAsync(AcmeId);
        await queue.OpenRequestAsync(title);
        var id = UrlIdParser.LastGuid(_page.Url);

        // ── Tab 1: open the editor and select a new status, but do not save yet (loads Version v1) ──
        await detail.SelectStatusAsync("Planned");

        // ── Tab 2 (same context / persona): load the same request and save first, bumping its Version ──
        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherDetail = new AdminSupportRequestDetailPage(otherPage, _fixture.AdminWebBaseUrl);
            await otherDetail.GoToAsync(AcmeId, id);
            await otherDetail.SelectStatusAsync("WaitingForCustomer");
            await otherDetail.SaveAsync();
            Assert.True(await otherDetail.IsSuccessMessageVisibleAsync(),
                "Expected the second tab's save to succeed and bump the request's Version");
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // ── Tab 1: saving now is stale → conflict banner, page stays, selection preserved ──
        await detail.SaveExpectingConflictAsync();

        Assert.True(await detail.IsConflictBannerVisibleAsync(),
            "Expected the optimistic-concurrency conflict banner after a stale status save");
        Assert.Contains($"/support-requests/{id}", _page.Url);
        Assert.Equal("Planned", await detail.GetSelectedStatusAsync());

        // ── Tab 1: "Reload latest values" clears the banner and adopts the other tab's winning value ──
        await detail.ClickReloadLatestValuesAsync();

        Assert.False(await detail.IsConflictBannerVisibleAsync(),
            "Expected the conflict banner to clear after reloading latest values");
        Assert.Equal("WaitingForCustomer", await detail.GetSelectedStatusAsync());

        // ── Tab 1: re-select against the fresh version and save successfully ──
        await detail.SelectStatusAsync("Resolved");
        await detail.SaveAsync();

        Assert.True(await detail.IsSuccessMessageVisibleAsync());
        Assert.False(await detail.IsConflictBannerVisibleAsync());

        await detail.GoToAsync(AcmeId, id);
        Assert.Equal("Resolved", await detail.GetSelectedStatusAsync());
    }

    /// <summary>
    /// Submits a uniquely-titled support request as a staff HR.Web persona (Laura) via the
    /// Help &amp; Feedback page, so each test contends only with its own seed data (deterministic
    /// at maxParallelThreads=15). Returns the generated title.
    /// </summary>
    private async Task<string> SeedSupportRequestAsync(string label)
    {
        var title = $"{label} {Guid.NewGuid().ToString("N")[..8]}";

        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var help = new HelpFeedbackPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await help.GoToAsync(AcmeId);
        await help.SelectTypeAsync("Ask a Question");
        await help.FillTitleAsync(title);
        await help.FillDescriptionAsync("Created by E2E test — please ignore.");
        await help.SelectPriorityAsync("Low");
        await help.SubmitAsync();

        return title;
    }
}
