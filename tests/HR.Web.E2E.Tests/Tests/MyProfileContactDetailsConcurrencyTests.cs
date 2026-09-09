using System.Net.Http.Json;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 2: optimistic-concurrency conflict UI on the self-service My Profile &gt; Contact Details
/// tab (MyProfileContactDetailsTab.razor). When a save fails because the employee record changed
/// since it was loaded (HTTP 409), the tab shows an <c>alert alert-warning</c> banner
/// ("Someone else changed your details while you were editing. Your changes have not been saved.")
/// plus a "Reload latest values" button that re-fetches the record, repopulates the form and
/// clears the banner. The caller's entered values are preserved until they choose to reload.
///
/// The "second editor" is simulated with a second browser tab in the same authenticated context
/// that loads the same Contact Details tab and saves first, bumping the record's version.
///
/// Uses the dedicated pre-seeded pool employee <see cref="SeededE2eEmployees.ConcurrencySelf"/>
/// (its Employee row is seeded; a real Supabase login is provisioned at runtime via the dev-only
/// ensure-employee-login endpoint) so no other parallel test class mutates its contact details.
/// </summary>
public sealed class MyProfileContactDetailsConcurrencyTests(EmployeePersonaFixture fixture)
    : SupabaseAuthSerialEmployeeTestBase(fixture)
{
    private static readonly Guid AcmeId = SeededE2eEmployees.AcmeCompanyId;

    private static readonly Guid EmployeeId = SeededE2eEmployees.ConcurrencySelf.EmployeeId;
    private const string EmployeeEmail = "e2e.seed50@acme.example";
    private static readonly string EmployeeLastName = SeededE2eEmployees.ConcurrencySelf.LastName;

    [Fact]
    public async Task ContactDetails_SaveAfterAnotherActorChangedRecord_ShowsWarningBanner_ThenReloadRecovers()
    {
        const string firstTabMobile = "07700 900161";
        const string otherTabMobile = "07700 900162";
        const string finalMobile    = "07700 900163";

        await EnsureEmployeeLoginAsync(EmployeeId, EmployeeEmail, EmployeeLastName);

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var contact = new ContactDetailsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(EmployeeEmail);

        // ── Tab 1: open Contact Details and start editing (loads the current version) ──
        await profile.GoToAsync(AcmeId, EmployeeId);
        await profile.OpenContactDetailsTabAsync();
        await contact.WaitForLoadAsync();
        await contact.FillAddressLine1Async("1 Concurrency Way");
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("EC1A 1BB");
        await contact.FillMobilePhoneAsync(firstTabMobile);

        // ── Tab 2 (same context): load the same tab and save a change first ──
        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherProfile = new MyProfilePage(otherPage, _fixture.WebBaseUrl);
            var otherContact = new ContactDetailsTab(otherPage);
            await otherProfile.GoToAsync(AcmeId, EmployeeId);
            await otherProfile.OpenContactDetailsTabAsync();
            await otherContact.WaitForLoadAsync();
            await otherContact.FillAddressLine1Async("2 Concurrency Way");
            await otherContact.FillCityAsync("London");
            await otherContact.FillPostCodeAsync("EC1A 1BB");
            await otherContact.FillMobilePhoneAsync(otherTabMobile);
            await otherContact.SaveChangesAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        // ── Tab 1: saving now is stale → warning banner, input preserved, no success ──
        await contact.ClickSaveExpectingConflictAsync();

        Assert.True(await contact.IsConcurrencyWarningVisibleAsync(),
            "Expected the optimistic-concurrency warning banner after a stale save");
        Assert.False(await contact.IsSuccessBannerVisibleAsync(),
            "Save should not have succeeded after a concurrency conflict");
        Assert.Equal(firstTabMobile, await contact.GetMobilePhoneValueAsync());

        // ── Tab 1: "Reload latest values" clears the banner and shows the other tab's value ──
        await contact.ClickReloadLatestValuesAsync();

        Assert.False(await contact.IsConcurrencyWarningVisibleAsync(),
            "Expected the warning banner to clear after reloading latest values");
        Assert.Equal(otherTabMobile, await contact.GetMobilePhoneValueAsync());

        // ── Tab 1: re-edit against the fresh version and save successfully ──
        await contact.FillMobilePhoneAsync(finalMobile);
        await contact.SaveChangesAsync();

        Assert.True(await contact.IsSuccessBannerVisibleAsync(),
            "Expected a success banner after saving against the reloaded version");
    }

    /// <summary>
    /// Bug (b): once the concurrency banner is showing on the Contact Details tab, editing a field
    /// to an invalid value (here: clearing the required "Address Line 1") must clear the stale
    /// banner so it doesn't sit alongside the new validation errors. Driven by
    /// EditSectionBase.OnValidationStateChanged.
    /// Shares the pool employee with the test above — the base runs employee tests serially and
    /// each test starts from a fresh login + page load, so there is no shared mutable state.
    /// </summary>
    [Fact]
    public async Task ContactDetails_MakingFieldInvalidAfterConflict_ClearsWarningBanner()
    {
        const string firstTabMobile = "07700 900171";
        const string otherTabMobile = "07700 900172";

        await EnsureEmployeeLoginAsync(EmployeeId, EmployeeEmail, EmployeeLastName);

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var contact = new ContactDetailsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(EmployeeEmail);

        await profile.GoToAsync(AcmeId, EmployeeId);
        await profile.OpenContactDetailsTabAsync();
        await contact.WaitForLoadAsync();
        await contact.FillAddressLine1Async("1 Bug B Way");
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("EC1A 1BB");
        await contact.FillMobilePhoneAsync(firstTabMobile);

        var otherPage = await _context.NewPageAsync();
        try
        {
            var otherProfile = new MyProfilePage(otherPage, _fixture.WebBaseUrl);
            var otherContact = new ContactDetailsTab(otherPage);
            await otherProfile.GoToAsync(AcmeId, EmployeeId);
            await otherProfile.OpenContactDetailsTabAsync();
            await otherContact.WaitForLoadAsync();
            await otherContact.FillAddressLine1Async("2 Bug B Way");
            await otherContact.FillCityAsync("London");
            await otherContact.FillPostCodeAsync("EC1A 1BB");
            await otherContact.FillMobilePhoneAsync(otherTabMobile);
            await otherContact.SaveChangesAsync();
        }
        finally
        {
            await otherPage.CloseAsync();
        }

        await contact.ClickSaveExpectingConflictAsync();
        Assert.True(await contact.IsConcurrencyWarningVisibleAsync());

        await contact.MakeFormInvalidAsync();

        Assert.False(await contact.IsConcurrencyWarningVisibleAsync(),
            "Expected the concurrency banner to clear once the form became invalid after a conflict");
    }

    /// <summary>
    /// Gives the pre-seeded (login-less) pool employee a real, working Supabase login via the
    /// dev-only POST /api/dev/ensure-employee-login endpoint — same helper pattern as
    /// SelfServiceDocumentTests. 404s outside Development.
    /// </summary>
    private async Task EnsureEmployeeLoginAsync(Guid employeeId, string email, string lastName)
    {
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        HttpResponseMessage? response = null;
        string? body = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            response = await http.PostAsJsonAsync("/api/dev/ensure-employee-login", new
            {
                EmployeeId = employeeId,
                CompanyId  = AcmeId,
                Email      = email,
                FirstName  = "E2E",
                LastName   = lastName,
            });

            if (response.IsSuccessStatusCode) return;

            body = await response.Content.ReadAsStringAsync();
            if (attempt < 3) await Task.Delay(1000 * attempt);
        }

        Assert.True(response!.IsSuccessStatusCode,
            $"Expected /api/dev/ensure-employee-login to succeed, got {response.StatusCode}. Response body: {body}");
    }
}
