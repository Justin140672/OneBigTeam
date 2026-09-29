using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

public interface IPersonaFixture
{
    string WebBaseUrl { get; }
    string MarketingBaseUrl { get; }
    string ApiBaseUrl { get; }
    string AdminWebBaseUrl { get; }
    IBrowser Browser { get; }

    BrowserNewContextOptions? AuthenticatedContextOptions { get; }

    bool RequiresFullTeardownDelay { get; }
}
