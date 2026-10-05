using System.Net.Http.Json;
using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Ticket 9: bulk employee invitation excludes employees whose work email is on a public/personal
/// email domain (server-side, QueueInvitationBatchHandler -> AccountCreationEmailGuard).
///
/// The normal Employees grid's client-side preview (EmployeeList.OnInviteSelectedClicked) does NOT
/// classify email domains — the server is authoritative — so a gmail employee appears as a
/// recipient in the confirm dialog and is only excluded once the batch is queued:
///  • mixed selection -> batch queued for the organisation-email employee; the page shows the
///    "[data-testid='invite-batch-excluded']" warning naming the gmail employee + explanation;
///  • all-public selection -> the API returns 400 work_email_required and the dialog stays open
///    showing its ".alert-danger" error (naming the rejected address).
///
/// Arrange creates fresh, uniquely-named employees in Acme via POST
/// /api/companies/{companyId}/employees under Laura Bennett's dev-persona session — the same fast
/// API arrange ManagerTeamProfileTests.CreateEmployeeViaApiAsync / EmployeeNotesTabTests use,
/// rather than the multi-combobox New Employee form. Creating an employee record with a gmail
/// work email is permitted (it isn't account creation); only inviting it is blocked. Each test
/// uses its own Guid token, so tests never share rows even at maxParallelThreads=15.
/// </summary>
public sealed class BulkEmployeeInvitationWorkEmailPolicyTests(HrAdminPersonaFixture fixture)
    : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid LauraUserId = Guid.Parse("30000000-0000-0000-0000-000000000005");
    private const string HrAdminEmail = "laura.bennett@acme.example";

    private static readonly Guid DepartmentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LocationId = Guid.Parse("70000000-0000-0000-0000-000000000001");
    private static readonly Guid PositionProfileId = Guid.Parse("20000000-0000-0000-0000-00000000000B");
    private static readonly Guid EmploymentTypeId = Guid.Parse("40000000-0000-0000-0000-000000000001");

    private const string ExclusionExplanation =
        "Organisation email required. An organisation email address is required to create an account.";

    [Fact]
    public async Task MixedSelection_QueuesOrgEmployee_AndListsPublicEmailEmployeeAsNotInvited()
    {
        var token = NewToken();
        var gmailLastName = $"WepGmail{token}";
        var orgLastName = $"WepOrg{token}";
        var gmailEmail = $"e2e.wep.{token}@gmail.com";
        var orgEmail = $"e2e.wep.{token}@acme.example";

        await CreateEmployeeViaApiAsync(gmailLastName, gmailEmail, token + "G");
        await CreateEmployeeViaApiAsync(orgLastName, orgEmail, token + "O");

        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);
        var progressPanel = new InvitationBatchProgressPanelPage(_page);

        await LoginAndOpenEmployeeListAsync(empList);

        await empList.SearchAsync(token);
        await empList.CheckEmployeeRowAsync(gmailLastName);
        await empList.CheckEmployeeRowAsync(orgLastName);

        await empList.ClickInviteSelectedToolbarButtonAsync();
        await confirmDialog.WaitForVisibleAsync();

        var previewEmails = await confirmDialog.GetRecipientEmailsAsync();
        Assert.Contains(previewEmails, e => e.Equals(gmailEmail, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(previewEmails, e => e.Equals(orgEmail, StringComparison.OrdinalIgnoreCase));

        await confirmDialog.SendAsync();

        await Assertions.Expect(empList.InviteBatchExcludedAlert).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(empList.InviteBatchExcludedAlert).ToContainTextAsync("1 employee(s) were not invited:");
        await Assertions.Expect(empList.InviteBatchExcludedRows).ToHaveCountAsync(1);

        var gmailRow = empList.InviteBatchExcludedRows.First;
        await Assertions.Expect(gmailRow).ToContainTextAsync(gmailLastName);
        await Assertions.Expect(gmailRow).ToContainTextAsync($"({gmailEmail})");
        await Assertions.Expect(gmailRow).ToContainTextAsync(ExclusionExplanation);
        await Assertions.Expect(empList.InviteBatchExcludedAlert).Not.ToContainTextAsync(orgLastName);

        await progressPanel.WaitForVisibleAsync();
        await Assertions.Expect(progressPanel.RecipientRows.Filter(new() { HasText = orgEmail }))
            .ToHaveCountAsync(1, new() { Timeout = 20_000 });
        await Assertions.Expect(progressPanel.RecipientRows.Filter(new() { HasText = gmailEmail }))
            .ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AllPublicEmailSelection_DialogShowsWorkEmailRequiredError_AndStaysOpen()
    {
        var token = NewToken();
        var gmailLastName = $"WepAllGmail{token}";
        var hotmailLastName = $"WepAllHotmail{token}";
        var gmailEmail = $"e2e.wep.all.{token}@gmail.com";
        var hotmailEmail = $"e2e.wep.all.{token}@hotmail.com";

        await CreateEmployeeViaApiAsync(gmailLastName, gmailEmail, token + "G");
        await CreateEmployeeViaApiAsync(hotmailLastName, hotmailEmail, token + "H");

        var empList = new EmployeeListPage(_page, _fixture.WebBaseUrl);
        var confirmDialog = new BulkInviteConfirmDialogPage(_page);

        await LoginAndOpenEmployeeListAsync(empList);

        await empList.SearchAsync(token);
        await empList.CheckEmployeeRowAsync(gmailLastName);
        await empList.CheckEmployeeRowAsync(hotmailLastName);

        await empList.ClickInviteSelectedToolbarButtonAsync();
        await confirmDialog.WaitForVisibleAsync();

        await confirmDialog.ClickSendAsync();

        await Assertions.Expect(confirmDialog.ErrorAlert).ToBeVisibleAsync(new() { Timeout = 20_000 });
        // QueueInvitationBatchHandler's all-rejected message names every rejected address.
        await Assertions.Expect(confirmDialog.ErrorAlert)
            .ToContainTextAsync("An organisation email address is required to create an account.");
        await Assertions.Expect(confirmDialog.ErrorAlert).ToContainTextAsync(gmailEmail);
        await Assertions.Expect(confirmDialog.ErrorAlert).ToContainTextAsync(hotmailEmail);

        Assert.True(await confirmDialog.IsVisibleAsync(), "Expected the confirm dialog to stay open after the rejection.");
        await Assertions.Expect(empList.InviteBatchExcludedAlert).ToHaveCountAsync(0);

        await confirmDialog.CancelAsync();
    }


    private static string NewToken() => Guid.NewGuid().ToString("N")[..10];

    private async Task LoginAndOpenEmployeeListAsync(EmployeeListPage empList)
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(HrAdminEmail);
        await empList.GoToAsync(AcmeId);
    }

    private async Task CreateEmployeeViaApiAsync(string lastName, string workEmail, string employeeNumberSuffix)
    {
        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };

        HttpResponseMessage? sessionResponse = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            sessionResponse = await http.PostAsync($"/api/dev/persona/{LauraUserId}", content: null);
            if (sessionResponse.IsSuccessStatusCode) break;
            if (attempt < 3) await Task.Delay(1000 * attempt);
        }
        Assert.True(sessionResponse!.IsSuccessStatusCode,
            $"Expected /api/dev/persona/{{userId}} to succeed, got {sessionResponse.StatusCode}.");
        var session = await sessionResponse.Content.ReadFromJsonAsync<DevPersonaSessionResult>();
        Assert.NotNull(session);
        http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", session!.AccessToken);

        var response = await http.PostAsJsonAsync(
            $"/api/companies/{AcmeId}/employees",
            new
            {
                companyId = AcmeId,
                departmentId = DepartmentId,
                locationId = LocationId,
                positionProfileId = PositionProfileId,
                firstName = "E2E",
                lastName,
                workEmail,
                startDate = "2026-03-01",
                dateOfBirth = "1990-06-15",
                nationality = "British",
                addressLine1 = "1 Test Street",
                city = "London",
                postCode = "SW1A 1AA",
                gender = "Male",
                employeeNumber = $"E2E-WEP-{employeeNumberSuffix}",
                employmentTypeId = EmploymentTypeId,
                hasSystemAccess = true,
            });
        Assert.True(response.IsSuccessStatusCode,
            $"Expected employee creation to succeed, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private sealed record DevPersonaSessionResult(string AccessToken, string RefreshToken, int ExpiresIn);
}
