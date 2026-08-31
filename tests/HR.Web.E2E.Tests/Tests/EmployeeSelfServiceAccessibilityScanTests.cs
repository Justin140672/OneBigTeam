using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// NFR-05: applies the shared <see cref="AccessibilityScan"/> axe-core WCAG 2.0 A/AA gate to the
/// employee self-service journeys as Tom Williams — his My Profile main (Overview) tab, the Leave
/// tab, and the Request Leave dialog open with its form visible.
/// </summary>
public sealed class EmployeeSelfServiceAccessibilityScanTests(EmployeePersonaFixture fixture)
    : RoleE2ETestBase<EmployeePersonaFixture>(fixture)
{
    private static readonly Guid AcmeId    = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TomId     = Guid.Parse("30000000-0000-0000-0000-000000000004");
    private const string TomEmail = "tom.williams@acme.example";

    private async Task<MyProfilePage> OpenProfileAsync()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();
        await login.LoginAsync(TomEmail);

        var profile = new MyProfilePage(_page, _fixture.WebBaseUrl);
        await profile.GoToAsync(AcmeId, TomId);
        return profile;
    }

    [Fact]
    public async Task MyProfileOverviewTab_HasNoSeriousViolations()
    {
        var profile = await OpenProfileAsync();
        await profile.OpenOverviewTabAsync();

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "my profile — Overview tab");
    }

    [Fact]
    public async Task MyProfileLeaveTab_HasNoSeriousViolations()
    {
        var profile = await OpenProfileAsync();
        await profile.OpenLeaveTabAsync();

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "my profile — Leave tab");
    }

    [Fact]
    public async Task RequestLeaveDialog_Open_HasNoSeriousViolations()
    {
        var profile = await OpenProfileAsync();
        await profile.OpenLeaveTabAsync();
        await profile.ClickRequestLeaveAsync();

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "Request Leave dialog (open)");
    }
}
