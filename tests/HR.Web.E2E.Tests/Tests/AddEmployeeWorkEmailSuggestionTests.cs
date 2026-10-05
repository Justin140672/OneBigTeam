using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class AddEmployeeWorkEmailSuggestionTests(HrSettingsSerialFixture fixture) : HrSettingsSerialTestBase(fixture)
{
    private static readonly Guid BetaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private const string BetaHrAdminEmail = "grace.kim@betacorp.example";
    private const string PrimaryDomain = "e2e-suggest.example.com";
    private const string AlternativeDomain = "alt-suggest.example.com";
    private const string NoteText = "Accepting a suggestion records the address in the HR system. It does not create the mailbox.";

    private async Task WithSuggestionsConfiguredAsync(string additionalDomains, Func<Task> body)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var hrSettings = new HrSettingsPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(BetaHrAdminEmail);

        await hrSettings.GoToWorkEmailTabAsync(BetaCorpId);
        var initialEnabled = await hrSettings.IsWorkEmailSuggestionsEnabledAsync();
        var initialPrimary = await hrSettings.GetWorkEmailPrimaryDomainAsync();
        var initialAdditional = await hrSettings.GetWorkEmailAdditionalDomainsAsync();
        var initialConvention = await hrSettings.GetWorkEmailConventionAsync();

        try
        {
            await hrSettings.ConfigureWorkEmailAsync(true, PrimaryDomain, additionalDomains, "firstname.lastname");
            await Assertions.Expect(hrSettings.WorkEmailSaved).ToBeVisibleAsync();

            await body();
        }
        finally
        {
            await hrSettings.GoToWorkEmailTabAsync(BetaCorpId);
            await hrSettings.ConfigureWorkEmailAsync(initialEnabled, initialPrimary, initialAdditional, initialConvention);
        }
    }

    private async Task FillBetaEmployeeAsync(EmployeeEditPage empEdit, string firstName, string lastName, string? workEmail)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];

        await empEdit.FillFirstNameAsync(firstName);
        await empEdit.FillLastNameAsync(lastName);
        if (workEmail is not null)
            await empEdit.FillWorkEmailAsync(workEmail);
        await empEdit.FillRequiredAddressAsync();
        await empEdit.FillRequiredCompensationAsync();
        await empEdit.SelectDropdownAsync("Gender", "Male");
        await empEdit.SelectDropdownAsync("Nationality", "British");
        await empEdit.FillDateOfBirthAsync("15/06/1990");
        await empEdit.FillStartDateAsync("01/03/2026");
        await empEdit.FillEmployeeNumberAsync($"E2E-{unique}");
        await empEdit.SelectDropdownAsync("Employment Type", "Permanent");
        await empEdit.SelectDropdownAsync("Position Profile", "Software Developer");
    }

    [Fact]
    public async Task EnteringNames_ShowsSuggestion_AndUseSuggestionFillsWorkEmail()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"Sugg{unique}";
        var expected = $"jane.sugg{unique}@{PrimaryDomain}";

        await WithSuggestionsConfiguredAsync("", async () =>
        {
            var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
            await empEdit.GoToNewAsync(BetaCorpId);

            await Assertions.Expect(empEdit.WorkEmailSuggestionStatus).ToHaveAttributeAsync("role", "status");
            await Assertions.Expect(empEdit.WorkEmailSuggestionStatus).ToHaveAttributeAsync("aria-live", "polite");
            await Assertions.Expect(empEdit.UseWorkEmailSuggestionButton).ToHaveCountAsync(0);

            await empEdit.FillFirstNameAsync("Jane");
            Assert.Equal(0, await empEdit.UseWorkEmailSuggestionButton.CountAsync());

            await empEdit.FillLastNameAsync(lastName);

            await Assertions.Expect(empEdit.WorkEmailSuggestionStatus)
                .ToContainTextAsync(expected, new() { Timeout = 20_000 });

            var useButton = _page.GetByRole(AriaRole.Button, new() { Name = $"Use suggested work email {expected}", Exact = true });
            await Assertions.Expect(useButton).ToBeVisibleAsync();
            await Assertions.Expect(empEdit.WorkEmailSuggestionNote).ToBeVisibleAsync();
            await Assertions.Expect(empEdit.WorkEmailSuggestionNote).ToHaveTextAsync(NoteText);
            await Assertions.Expect(empEdit.WorkEmailDomainField).ToHaveCountAsync(0);

            await useButton.ClickAsync();

            await Assertions.Expect(empEdit.WorkEmailInput).ToHaveValueAsync(expected);
            await Assertions.Expect(empEdit.UseWorkEmailSuggestionButton).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task ManuallyTypedWorkEmail_IsNotOverwritten_WhenNamesChange()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var manualEmail = $"manual.{unique}@elsewhere.example.com";

        await WithSuggestionsConfiguredAsync("", async () =>
        {
            var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
            await empEdit.GoToNewAsync(BetaCorpId);

            await empEdit.FillFirstNameAsync("Jane");
            await empEdit.FillLastNameAsync($"First{unique}");
            await Assertions.Expect(empEdit.WorkEmailSuggestionStatus)
                .ToContainTextAsync($"jane.first{unique}@{PrimaryDomain}", new() { Timeout = 20_000 });

            await empEdit.TypeWorkEmailWithoutLeavingFieldAsync(manualEmail);

            await empEdit.FillLastNameAsync($"Second{unique}");
            await Assertions.Expect(empEdit.WorkEmailSuggestionStatus)
                .ToContainTextAsync($"jane.second{unique}@{PrimaryDomain}", new() { Timeout = 20_000 });
            await Assertions.Expect(empEdit.UseWorkEmailSuggestionButton).ToBeVisibleAsync();

            await Assertions.Expect(empEdit.WorkEmailInput).ToHaveValueAsync(manualEmail);
        });
    }

    [Fact]
    public async Task DuplicateSuggestion_ShowsUnavailableMessage_WithoutUseButtonOrNumberedAlternative()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"Dup{unique}";
        var takenEmail = $"jane.dup{unique}@{PrimaryDomain}";

        await WithSuggestionsConfiguredAsync("", async () =>
        {
            var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

            await empEdit.GoToNewAsync(BetaCorpId);
            await FillBetaEmployeeAsync(empEdit, "Jane", lastName, takenEmail);
            await empEdit.SaveNewEmployeeAsync();

            await empEdit.GoToNewAsync(BetaCorpId);
            await empEdit.FillFirstNameAsync("Jane");
            await empEdit.FillLastNameAsync(lastName);

            await Assertions.Expect(empEdit.WorkEmailSuggestionStatus)
                .ToHaveTextAsync($"{takenEmail} is already in use. Enter a work email manually.", new() { Timeout = 20_000 });
            await Assertions.Expect(empEdit.UseWorkEmailSuggestionButton).ToHaveCountAsync(0);
            await Assertions.Expect(empEdit.WorkEmailSuggestionNote).ToHaveTextAsync(NoteText);
            await Assertions.Expect(empEdit.WorkEmailInput).ToHaveValueAsync("");
        });
    }

    [Fact]
    public async Task MultipleDomains_ShowDomainDropdown_AndSuggestionFollowsSelectedDomain()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var lastName = $"Multi{unique}";

        await WithSuggestionsConfiguredAsync(AlternativeDomain, async () =>
        {
            var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);
            await empEdit.GoToNewAsync(BetaCorpId);

            await empEdit.FillFirstNameAsync("Jane");
            await empEdit.FillLastNameAsync(lastName);

            await Assertions.Expect(empEdit.WorkEmailSuggestionStatus)
                .ToContainTextAsync($"jane.multi{unique}@{PrimaryDomain}", new() { Timeout = 20_000 });
            await Assertions.Expect(empEdit.WorkEmailDomainField).ToBeVisibleAsync();

            await empEdit.SelectWorkEmailSuggestionDomainAsync(AlternativeDomain);

            var expected = $"jane.multi{unique}@{AlternativeDomain}";
            await Assertions.Expect(empEdit.WorkEmailSuggestionStatus)
                .ToContainTextAsync(expected, new() { Timeout = 20_000 });

            await _page.GetByRole(AriaRole.Button, new() { Name = $"Use suggested work email {expected}", Exact = true }).ClickAsync();
            await Assertions.Expect(empEdit.WorkEmailInput).ToHaveValueAsync(expected);
        });
    }
}
