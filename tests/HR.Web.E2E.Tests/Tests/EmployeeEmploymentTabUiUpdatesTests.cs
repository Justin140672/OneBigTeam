using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// Verifies two Employee Edit page layout changes on the Employment tab
/// (EmployeeEmploymentTab.razor):
/// - "Hours"/"FTE"/"Effective From" read-only fields render alongside "Current Salary".
/// - The "Organisation" card renders above the "Dates" card.
///
/// Uses the seeded "Sarah Chen" employee (ID: 30000000-0000-0000-0000-000000000001), who has a
/// current compensation record (see EmployeeCompensationTabTests' own header comment), so her
/// Hours/FTE/Effective From fields are populated rather than showing the "—" empty-state.
/// </summary>
[Collection("E2E")]
public sealed class EmployeeEmploymentTabUiUpdatesTests(AppFixture fixture) : E2ETestBase(fixture)
{
    private static readonly Guid AcmeId    = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SarahChen = Guid.Parse("30000000-0000-0000-0000-000000000001");

    private const string LauraEmail = "laura.bennett@acme.example";

    [Fact]
    public async Task EmploymentTab_ShowsHoursFteAndEffectiveFrom_AlongsideCurrentSalary()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);
        await empEdit.OpenEmploymentTabAsync();

        var salary = await empEdit.GetEmploymentTabReadOnlyFieldAsync("Current Salary");
        Assert.NotNull(salary);
        Assert.Contains("145,000.00", salary);

        var hours = await empEdit.GetEmploymentTabReadOnlyFieldAsync("Hours");
        Assert.NotNull(hours);
        Assert.Contains("37.5", hours);

        var fte = await empEdit.GetEmploymentTabReadOnlyFieldAsync("FTE");
        Assert.NotNull(fte);
        Assert.Contains("100", fte);

        var effectiveFrom = await empEdit.GetEmploymentTabReadOnlyFieldAsync("Effective From");
        Assert.NotNull(effectiveFrom);
        Assert.Contains("2023", effectiveFrom);
    }

    [Fact]
    public async Task EmploymentTab_OrganisationCard_RendersAboveDatesCard()
    {
        var login   = new LoginPage(_page, _fixture.WebBaseUrl);
        var empEdit = new EmployeeEditPage(_page, _fixture.WebBaseUrl);

        await login.GoToAsync();
        await login.LoginAsync(LauraEmail);

        await empEdit.GoToAsync(AcmeId, SarahChen);
        await empEdit.OpenEmploymentTabAsync();

        var headings = await empEdit.GetEmploymentTabCardHeadingsAsync();

        var organisationIndex = headings.ToList().IndexOf("Organisation");
        var datesIndex = headings.ToList().IndexOf("Dates");

        Assert.True(organisationIndex >= 0, "Expected an 'Organisation' card on the Employment tab");
        Assert.True(datesIndex >= 0, "Expected a 'Dates' card on the Employment tab");
        Assert.True(organisationIndex < datesIndex,
            "Expected the 'Organisation' card to render above the 'Dates' card");
    }
}
