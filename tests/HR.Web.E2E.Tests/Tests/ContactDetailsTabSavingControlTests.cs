using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 7 follow-up (round 3): the "in-flight save" accessibility + duplicate-submit-guard
/// behaviour for the self-service My Profile &gt; Contact Details tab. The self-service save goes
/// server-side (HR.Web → hrapi), so a Playwright browser-route interceptor cannot hold or fail it.
/// These tests instead drive HR.Web's E2E_TESTING-only <c>/_e2e/contact-save-control/{email}</c>
/// endpoints via <see cref="ContactDetailsTab.SaveControl"/>, which hold the next server-side PUT
/// for one specific employee email.
///
/// Each test uses its OWN dedicated pool employee (<see cref="SeededE2eEmployees.ContactSaveControl"/>,
/// seeds 51-54) so the per-email control store gives it full isolation. The class serialises real
/// Supabase logins via <see cref="SupabaseAuthSerialEmployeeTestBase"/>.
/// </summary>
public sealed class ContactDetailsTabSavingControlTests(EmployeePersonaFixture fixture)
    : SupabaseAuthSerialEmployeeTestBase(fixture)
{
    private static readonly Guid AcmeId = SeededE2eEmployees.AcmeCompanyId;

    private static readonly SeededE2eEmployees.Pooled HeldSaveEmployee     = SeededE2eEmployees.ContactSaveControl[0];
    private static readonly SeededE2eEmployees.Pooled FailRetryEmployee    = SeededE2eEmployees.ContactSaveControl[1];
    private static readonly SeededE2eEmployees.Pooled KeyboardDismissEmployee = SeededE2eEmployees.ContactSaveControl[2];
    private static readonly SeededE2eEmployees.Pooled BoundedFailureEmployee = SeededE2eEmployees.ContactSaveControl[3];

    private const string HeldSaveEmail        = "e2e.seed51@acme.example";
    private const string FailRetryEmail       = "e2e.seed52@acme.example";
    private const string KeyboardDismissEmail = "e2e.seed53@acme.example";
    private const string BoundedFailureEmail  = "e2e.seed54@acme.example";

    // ── Test 1: saving announcement + duplicate-submit guard ─────────────────

    [Fact]
    public async Task ContactDetailsTab_Saving_AnnouncesToAssistiveTechAndPreventsDuplicateSubmit()
    {
        var contact = await OpenContactDetailsAsync(HeldSaveEmployee, HeldSaveEmail);
        await FillValidUniqueAsync(contact);

        await using var ctrl = await ContactDetailsTab.SaveControl.ArmAsync(_fixture.WebBaseUrl, HeldSaveEmail);

        await contact.ClickSaveAsync();
        await ctrl.WaitUntilRequestArrivedAsync();

        // The live region announces the in-flight save and is exposed to assistive tech.
        Assert.Contains("Saving contact details", await contact.SavingStatusTextAsync());
        Assert.Equal("status", await contact.SavingStatusRegion.GetAttributeAsync("role"));
        Assert.Equal("polite", await contact.SavingStatusRegion.GetAttributeAsync("aria-live"));
        Assert.True(await contact.IsSaveDisabledAsync(),
            "Save button should be disabled while a save is in flight.");

        // Attempt keyboard re-activation WITHOUT any Playwright call that auto-waits for the button
        // to be enabled — focus the form via JS then press keys straight at the keyboard.
        await _page.EvaluateAsync("() => { const b = document.getElementById('cd-save-button'); if (b) b.focus(); }");
        await _page.Keyboard.PressAsync("Enter");
        await _page.Keyboard.PressAsync("Space");

        Assert.Equal(1, await ctrl.RequestCountAsync());

        await AccessibilityScan.AssertNoSeriousViolationsAsync(
            _page, "my profile — Contact Details tab (saving in flight)");

        await ctrl.ReleaseAsync();

        await _page.Locator(".cd-success-banner").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        Assert.Equal(string.Empty, await contact.SavingStatusTextAsync());
        Assert.False(await contact.IsSaveDisabledAsync(),
            "Save button should be re-enabled after a successful save.");
    }

    // ── Test 2: server failure → accessible error + retry ───────────────────

    [Fact]
    public async Task ContactDetailsTab_ServerFailure_ShowsAccessibleErrorAndAllowsRetry()
    {
        var contact = await OpenContactDetailsAsync(FailRetryEmployee, FailRetryEmail);
        var email = $"e2e.{Guid.NewGuid():N}@personal.example.com";
        var line1 = $"{Guid.NewGuid():N} Retry Street";
        await contact.FillAddressLine1Async(line1);
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("EC1A 1BB");
        await contact.FillPersonalEmailAsync(email);

        var ctrl = await ContactDetailsTab.SaveControl.ArmAsync(_fixture.WebBaseUrl, FailRetryEmail);
        try
        {
            await contact.ClickSaveAsync();
            await ctrl.WaitUntilRequestArrivedAsync();
            await ctrl.FailAsync();

            await _page.Locator(".alert-danger[role='alert']").WaitForAsync(
                new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            Assert.False(string.IsNullOrWhiteSpace(await contact.ErrorAlertTextAsync()));

            // Entered values preserved, saving status cleared, Save re-enabled.
            Assert.Equal(email, await contact.GetPersonalEmailAsync());
            Assert.Equal(line1, (await _page.GetByPlaceholder("Street address").InputValueAsync()).Trim());
            Assert.Equal(string.Empty, await contact.SavingStatusTextAsync());
            Assert.False(await contact.IsSaveDisabledAsync());

            await AccessibilityScan.AssertNoSeriousViolationsAsync(
                _page, "my profile — Contact Details tab (server error)");
        }
        finally
        {
            // Clear the control so the retry hits the real API.
            await ctrl.DisposeAsync();
        }

        await contact.SaveChangesAsync();
        Assert.True(await contact.IsSuccessBannerVisibleAsync());
    }

    // ── Test 3: keyboard-dismiss the error banner returns focus to Save ─────

    [Fact]
    public async Task ContactDetailsTab_ErrorBanner_DismissByKeyboard_ReturnsFocusToSave()
    {
        var contact = await OpenContactDetailsAsync(KeyboardDismissEmployee, KeyboardDismissEmail);
        await FillValidUniqueAsync(contact);

        await using var ctrl = await ContactDetailsTab.SaveControl.ArmAsync(_fixture.WebBaseUrl, KeyboardDismissEmail);
        await contact.ClickSaveAsync();
        await ctrl.WaitUntilRequestArrivedAsync();
        await ctrl.FailAsync();

        await _page.Locator(".alert-danger[role='alert']").WaitForAsync(
            new() { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        await contact.DismissErrorByKeyboardAsync("Space");

        Assert.Equal(string.Empty, await contact.ErrorAlertTextAsync());
        Assert.Equal("cd-save-button", await contact.FocusedElementIdAsync());
    }

    // ── Test 4: a deliberately-wrong expectation fails in a BOUNDED way and
    //            leaves no held request or shared control behind ──────────────

    [Fact]
    public async Task ContactDetailsTab_DeliberatelyFailedAssertion_ProducesBoundedFailure_AndLeavesNoPendingRequestOrSharedControl()
    {
        var contact = await OpenContactDetailsAsync(BoundedFailureEmployee, BoundedFailureEmail);
        await FillValidUniqueAsync(contact);

        var ctrl = await ContactDetailsTab.SaveControl.ArmAsync(_fixture.WebBaseUrl, BoundedFailureEmail);

        await contact.ClickSaveAsync();
        await ctrl.WaitUntilRequestArrivedAsync();

        // A wrong expectation still fails promptly (bounded), not by hanging.
        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            Assert.Equal(999, await ctrl.RequestCountAsync());
        });
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30),
            $"A wrong expectation should fail in a bounded time; took {sw.Elapsed.TotalSeconds:0.0}s.");

        // Cleanup releases the held request and removes the shared control.
        await ctrl.DisposeAsync();

        using var http = new HttpClient { BaseAddress = new Uri(_fixture.WebBaseUrl) };
        var afterDispose = await http.GetAsync($"/_e2e/contact-save-control/{BoundedFailureEmail}");
        Assert.Equal(HttpStatusCode.NotFound, afterDispose.StatusCode);

        // A fresh armed control on the same email starts clean — no leakage.
        await using var fresh = await ContactDetailsTab.SaveControl.ArmAsync(_fixture.WebBaseUrl, BoundedFailureEmail);
        Assert.Equal(0, await fresh.RequestCountAsync());
        var freshStatus = await http.GetFromJsonAsync<FreshStatus>(
            $"/_e2e/contact-save-control/{BoundedFailureEmail}");
        Assert.NotNull(freshStatus);
        Assert.False(freshStatus!.arrived, "A freshly armed control should not report a request as arrived.");
    }

    private sealed record FreshStatus(bool arrived, int requestCount, bool resolved);

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async Task<ContactDetailsTab> OpenContactDetailsAsync(SeededE2eEmployees.Pooled employee, string email)
    {
        await EnsureEmployeeLoginAsync(employee.EmployeeId, email, employee.LastName);

        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        var contact = new ContactDetailsTab(_page);

        await login.GoToAsync();
        await login.LoginAsync(email);

        await profile.GoToAsync(AcmeId, employee.EmployeeId);
        await profile.OpenContactDetailsTabAsync();
        await contact.WaitForLoadAsync();
        return contact;
    }

    /// <summary>Fills the mandatory fields plus a unique personal email so every saving test writes
    /// isolated data and can run at maxParallelThreads=15.</summary>
    private static async Task FillValidUniqueAsync(ContactDetailsTab contact)
    {
        await contact.FillAddressLine1Async($"{Guid.NewGuid():N} Test Street");
        await contact.FillCityAsync("London");
        await contact.FillPostCodeAsync("EC1A 1BB");
        await contact.FillPersonalEmailAsync($"e2e.{Guid.NewGuid():N}@personal.example.com");
    }

    /// <summary>
    /// Gives the pre-seeded (login-less) pool employee a real, working Supabase login via the
    /// dev-only POST /api/dev/ensure-employee-login endpoint. 404s outside Development.
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
