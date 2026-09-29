using HR.Web.E2E.Tests.Infrastructure;
using HR.Web.E2E.Tests.Infrastructure.PageObjects;

namespace HR.Web.E2E.Tests.Tests;

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
