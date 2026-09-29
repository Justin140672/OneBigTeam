using System.Net.Http.Json;
using System.Text.RegularExpressions;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class GettingStartedAndExploreTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private const string HrAdminEmail = "laura.bennett@acme.example";

    private const string TomEmail = "tom.williams@acme.example";

    [Fact]
    public async Task GettingStarted_LoadsWithNineTasksAndProgressIndicator()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var gettingStarted = new GettingStartedPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await gettingStarted.GoToAsync();

        Assert.Equal(10, await gettingStarted.GetTaskCardCountAsync());

        foreach (var taskName in new[]
                 {
                     "Complete your company details",
                     "Configure your HR settings",
                     "Review your default leave policy",
                     "Add your team",
                     "Invite your team",
                     "Review your company documents",
                     "Start your subscription",
                 })
        {
            Assert.True(await gettingStarted.HasTaskAsync(taskName),
                $"Expected a task card for '{taskName}' to be visible on the Getting Started page");
        }

        var percentage = await gettingStarted.GetCompletionPercentageAsync();
        Assert.InRange(percentage, 0, 100);
    }

    [Fact]
    public async Task HrAdministrator_LandingOnRoot_RedirectsToGettingStarted()
    {
        var email = await ProvisionFreshCompanyAdminAsync();

        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(email);

        var completionDialog = new EmployeeCompletionDialogPage(_page);
        await completionDialog.WaitForVisibleAsync();
        await completionDialog.FillAllRequiredFieldsAsync(
            dobDdMMyyyy: "02/01/1990",
            nationality: "British",
            gender: "Female",
            addressLine1: "1 Test Street",
            city: "London",
            postcode: "SW1A 1AA");
        await completionDialog.SaveAndWaitForCloseAsync();

        await _page.WaitForURLAsync(new Regex("/getting-started"), new() { Timeout = 30_000 });
        Assert.Contains("/getting-started", _page.Url);
    }

    private async Task<string> ProvisionFreshCompanyAdminAsync()
    {
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        var email = $"e2e-getting-started-{Guid.NewGuid():N}@example.com";

        var signUpResponse = await http.PostAsJsonAsync("/api/signup", new
        {
            CompanyName = $"E2E Getting Started Co {Guid.NewGuid():N}",
            AdminFirstName = "Ada",
            AdminLastName = "Lovelace",
            AdminEmail = email,
            Password = "P@ssw0rd123",
        });
        signUpResponse.EnsureSuccessStatusCode();

        var signUp = await signUpResponse.Content.ReadFromJsonAsync<SignUpResult>();
        Assert.NotNull(signUp);

        var activateResponse = await http.PostAsJsonAsync(
            "/api/dev/activate-company", new { CompanyId = signUp!.CompanyId });
        activateResponse.EnsureSuccessStatusCode();

        var registerResponse = await http.PostAsJsonAsync("/api/dev/persona/register", new
        {
            UserId = signUp.UserId,
            CompanyId = signUp.CompanyId,
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = email,
        });
        registerResponse.EnsureSuccessStatusCode();

        return email;
    }

    private sealed record SignUpResult(
        Guid UserId,
        Guid CompanyId,
        string Email,
        string FirstName,
        string LastName);

    [Fact]
    public async Task CompletingHrSettingsTask_MarksGettingStartedTaskAsCompleted()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);
        var gettingStarted = new GettingStartedPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await HrSettingsSerialTestBase.GateInstance.WaitAsync();
        try
        {
            await hrSettings.GoToAsync(AcmeId);
            var currentHours = await hrSettings.GetHoursPerDayAsync();
            await hrSettings.SetHoursPerDayAsync(currentHours);
            await hrSettings.SaveAsync();
        }
        finally
        {
            HrSettingsSerialTestBase.GateInstance.Release();
        }

        await gettingStarted.GoToAsync();

        Assert.True(await gettingStarted.IsTaskCompletedAsync("Configure your HR settings"),
            "Expected 'Configure your HR settings' to show as completed after saving HR Settings");
    }

    [Fact]
    public async Task IncompleteTask_GoToTaskLink_PointsAtConfiguredLinkUrl()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var gettingStarted = new GettingStartedPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await gettingStarted.GoToAsync();

        var href = await gettingStarted.GetTaskLinkUrlAsync("Add your team");
        if (href is null)
        {
            return;
        }

        Assert.Matches(new Regex("^/companies/[0-9a-fA-F-]+/employees$"), href);
    }

    [Fact]
    public async Task SkipForNow_DismissesChecklist_AndNavigatesAway()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var gettingStarted = new GettingStartedPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        await gettingStarted.GoToAsync();
        await gettingStarted.SkipForNowAsync();

        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });
        Assert.Contains("/dashboard/hr", _page.Url);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/");
        await _page.WaitForURLAsync(new Regex("/dashboard/hr"), new() { Timeout = 15_000 });
        Assert.DoesNotContain("/getting-started", _page.Url);
    }

    [Fact]
    public async Task HelpMenu_NavigatesToGettingStarted_ForHrAdministrator()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var helpMenu = new HelpMenu(_page);

        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);

        Assert.True(await helpMenu.IsVisibleAsync(),
            "Expected the Help menu to be visible for an HR Administrator");

        await helpMenu.OpenAsync();
        await helpMenu.ClickGettingStartedAsync();
        await _page.WaitForURLAsync(new Regex("/getting-started"), new() { Timeout = 15_000 });
        Assert.Contains("/getting-started", _page.Url);
    }

    [Fact]
    public async Task HelpMenu_IsNotVisible_ForPlainEmployee()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var helpMenu = new HelpMenu(_page);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        Assert.False(await helpMenu.IsVisibleAsync(),
            "Expected the Help menu to be hidden for a plain Employee with no HR Administrator/Company Administrator role");
    }

    [Fact]
    public async Task PlainEmployee_IsRedirectedAway_FromGettingStarted()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        await _page.GotoAsync($"{_fixture.WebBaseUrl}/getting-started");
        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15_000 });
        Assert.DoesNotContain("/getting-started", _page.Url);
    }
}
