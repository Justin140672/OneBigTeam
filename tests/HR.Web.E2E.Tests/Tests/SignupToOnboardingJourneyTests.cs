using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Covers the end-to-end signup journey: submitting the marketing site's "Start free trial" form
/// (/signup on the "marketing" Aspire resource) creates a brand-new company + admin user via
/// HR.Api's POST /api/signup, establishes that admin as the current dev-auth persona via
/// POST /api/dev/persona/register (this app's entire "login" mechanism — DevAuthHandler/
/// DevPersonaStore hold a single global mutable "current user" on the API process, not a
/// per-browser cookie), and redirects straight to {web}/getting-started — no separate LoginPage
/// step is needed for the newly-created admin.
///
/// SignUp.razor is a static (non-interactive) page with a plain HTML form posting to
/// /signup-submit, so this test drives it with a normal Playwright form fill + submit
/// (no Blazor circuit/interactivity wait needed).
///
/// The new company has no seed data, so all mandatory onboarding tasks start incomplete. Since
/// the redirect after signup lands on /getting-started without the new company's id anywhere in
/// the URL, this test recovers the id from the "Go to task" link href for the (mandatory, always
/// incomplete for a fresh company) "Complete your company details" task — its LinkUrl is
/// "/companies/{companyId}/edit" (see CompleteCompanyDetailsTask / GettingStartedPage) — rather
/// than hardcoding any shared company's id.
/// </summary>
[Collection("E2E")]
public sealed class SignupToOnboardingJourneyTests(AppFixture fixture) : E2ETestBase(fixture)
{
    [Fact]
    public async Task SignUp_RedirectsToGettingStarted_WithSevenIncompleteTasks_AndCompletingHrSettingsUpdatesProgress()
    {
        var companyName = $"E2E Signup Co {Guid.NewGuid():N}";
        var email = $"e2e-signup-{Guid.NewGuid():N}@example.com";
        const string password = "P@ssw0rd123";

        await _page.GotoAsync($"{_fixture.MarketingBaseUrl}/signup");
        await _page.FillAsync("#companyName", companyName);
        await _page.FillAsync("#firstName", "Ada");
        await _page.FillAsync("#lastName", "Lovelace");
        await _page.FillAsync("#email", email);
        await _page.FillAsync("#password", password);

        await _page.GetByRole(AriaRole.Button, new() { Name = "Start free trial" }).ClickAsync();

        await _page.WaitForURLAsync(new Regex("/getting-started"), new() { Timeout = 20_000 });
        Assert.Contains("/getting-started", _page.Url);

        var gettingStarted = new GettingStartedPage(_page, _fixture.WebBaseUrl);
        await gettingStarted.WaitForLoadAsync();

        Assert.Equal(7, await gettingStarted.GetTaskCardCountAsync());

        // Recover the new company's id from the mandatory, still-incomplete "Complete your
        // company details" task's "Go to task" link rather than hardcoding a shared company id.
        var href = await gettingStarted.GetTaskLinkUrlAsync("Complete your company details");
        Assert.NotNull(href);
        var match = Regex.Match(href!, "^/companies/(?<id>[0-9a-fA-F-]+)/edit$");
        Assert.True(match.Success, $"Expected a company-scoped edit link, got '{href}'");
        var companyId = Guid.Parse(match.Groups["id"].Value);

        var baselinePercentage = await gettingStarted.GetCompletionPercentageAsync();

        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);
        await hrSettings.GoToAsync(companyId);
        var currentHours = await hrSettings.GetHoursPerDayAsync();
        await hrSettings.SetHoursPerDayAsync(currentHours);
        await hrSettings.SaveAsync();

        await gettingStarted.GoToAsync();

        Assert.True(await gettingStarted.IsTaskCompletedAsync("Configure your HR settings"),
            "Expected 'Configure your HR settings' to show as completed after saving HR Settings for the new company");

        var updatedPercentage = await gettingStarted.GetCompletionPercentageAsync();
        Assert.True(updatedPercentage > baselinePercentage,
            $"Expected completion percentage to increase after completing a task (was {baselinePercentage}%, now {updatedPercentage}%)");
    }
}
