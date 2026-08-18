using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// Common surface <see cref="E2ETestBase"/> and test classes use regardless of which fixture (role
/// or cross-user) their collection/class wires up. Every implementation wraps the same shared
/// <see cref="SharedAppFixture"/> instance (one Aspire app + Postgres + browser for the whole
/// assembly) so tests across all collections still only ever talk to one running app.
/// </summary>
public interface IPersonaFixture
{
    string WebBaseUrl { get; }
    string MarketingBaseUrl { get; }
    string ApiBaseUrl { get; }
    string AdminWebBaseUrl { get; }
    IBrowser Browser { get; }

    /// <summary>
    /// Playwright context options carrying a pre-authenticated storageState for this fixture's
    /// canonical persona, or null for fixtures that must log in fresh per test (CrossUserFixture).
    /// </summary>
    BrowserNewContextOptions? AuthenticatedContextOptions { get; }

    /// <summary>
    /// True only for CrossUserFixture, whose tests switch persona mid-test via the dev persona
    /// switcher / cookie-based re-login and need the outgoing Blazor Server circuit to fully die
    /// before the next persona logs in. Role-fixed fixtures never switch persona within a test (an
    /// outlier LoginAsync call for a different persona still clears cookies and re-authenticates
    /// cleanly — see LoginPage), so they can skip the teardown delay entirely.
    /// </summary>
    bool RequiresFullTeardownDelay { get; }
}
