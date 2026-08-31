using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

/// <summary>
/// NFR-05: applies the shared <see cref="AccessibilityScan"/> axe-core WCAG 2.0 A/AA gate to the
/// unauthenticated <c>/login</c> page. Uses <see cref="ParallelBlankPersonaFixture"/> so the context
/// starts with no session and the real login form renders.
/// </summary>
public sealed class LoginAccessibilityScanTests(ParallelBlankPersonaFixture fixture)
    : RoleE2ETestBase<ParallelBlankPersonaFixture>(fixture)
{
    [Fact]
    public async Task LoginPage_Unauthenticated_HasNoSeriousViolations()
    {
        var login = new LoginPage(_page, _fixture.WebBaseUrl);
        await login.GoToAsync();

        await AccessibilityScan.AssertNoSeriousViolationsAsync(_page, "/login (unauthenticated)");
    }
}
