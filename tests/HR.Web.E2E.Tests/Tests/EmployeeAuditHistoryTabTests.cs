using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;
using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Tests;

public sealed class EmployeeAuditHistoryTabTests(HrAdminPersonaFixture fixture) : RoleE2ETestBase<HrAdminPersonaFixture>(fixture)
{
    private static readonly Guid AcmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomWilliams = Guid.Parse("30000000-0000-0000-0000-000000000004");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task AuditTab_IsVisible_On_Employee_Edit_Page()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, TomWilliams);

        await EmployeeEditPage.SelectOwningGroupAsync(_page, "Audit");
        await Assertions.Expect(EmployeeEditPage.SectionTab(_page, "Audit"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task AuditTab_ShowsNewlyCreatedCompensationEvent_And_ViewOpensDetailDialogWithBeforeAfter()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        var employee = await E2eEmployeeApi.CreateAcmeEmployeeAsync(_fixture.ApiBaseUrl, "AuditComp");

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, employee.Id);
        await empEdit.OpenCompensationTabAsync();

        await empEdit.ClickAddCompensationAsync();
        await empEdit.FillAddCompensationEffectiveFromAsync("01/03/2031");
        await empEdit.SelectAddCompensationSalaryTypeAsync("Annual");
        await empEdit.FillAddCompensationSalaryAsync("39500");
        await empEdit.FillAddCompensationCurrencyAsync("GBP");
        await empEdit.SubmitAddCompensationDialogAsync();

        await empEdit.OpenAuditTabAsync();

        var row = empEdit.AuditHistoryRow("Compensation record created");
        await Assertions.Expect(row.First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await empEdit.ClickViewAuditRowAsync("Compensation record created");

        Assert.True(await empEdit.HasAuditDetailDialogAsync(), "Expected the audit detail dialog to open");

        var dialogText = await empEdit.GetAuditDetailDialogTextAsync();
        Assert.Contains("Compensation record created", dialogText);
        Assert.Contains("Salary Type", dialogText);
        Assert.Contains("Annual", dialogText);
        Assert.Contains("GBP", dialogText);
        // AUD-03: the salary *amount* is deliberately never written to the audit trail (only the
        // structured, non-sensitive fields EffectiveFrom / SalaryType / Currency — see
        // CompensationRecordCreatedAuditEvent.After), so "39500" must NOT appear here.
        Assert.DoesNotContain("39500", dialogText);
        Assert.Contains("—", dialogText);

        await empEdit.CloseAuditDetailDialogAsync();
        Assert.False(await empEdit.HasAuditDetailDialogAsync(), "Expected the audit detail dialog to close");
    }
}
