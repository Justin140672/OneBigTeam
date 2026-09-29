using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

public abstract class RolePersonaFixtureBase(string personaEmail) : IAsyncLifetime, IPersonaFixture
{
    private AppFixture? _app;
    private BrowserNewContextOptions? _authenticatedContextOptions;

    public string PersonaEmail { get; } = personaEmail;

    public string WebBaseUrl => _app!.WebBaseUrl;
    public string MarketingBaseUrl => _app!.MarketingBaseUrl;
    public string ApiBaseUrl => _app!.ApiBaseUrl;
    public string AdminWebBaseUrl => _app!.AdminWebBaseUrl;
    public IBrowser Browser => _app!.Browser;
    public BrowserNewContextOptions? AuthenticatedContextOptions => _authenticatedContextOptions;
    public bool RequiresFullTeardownDelay => false;

    public async Task InitializeAsync()
    {
        _app = await SharedAppFixture.AcquireAsync();

        _authenticatedContextOptions = await PersonaLoginCache.GetOrLoginAsync(_app, PersonaEmail);
    }

    public async Task DisposeAsync() => await SharedAppFixture.ReleaseAsync();
}

public sealed class HrAdminPersonaFixture() : RolePersonaFixtureBase("laura.bennett@acme.example");

public sealed class ManagerPersonaFixture() : RolePersonaFixtureBase("james.okafor@acme.example");

public sealed class RecruiterPersonaFixture() : RolePersonaFixtureBase("marcus.diallo@acme.example");

public sealed class EmployeePersonaFixture() : RolePersonaFixtureBase("tom.williams@acme.example");
