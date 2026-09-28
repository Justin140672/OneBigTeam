using Microsoft.Playwright;

namespace HR.Web.E2E.Tests.Infrastructure;

/// <summary>
/// The single source of <see cref="BrowserNewContextOptions"/> for every browser context the E2E
/// suite creates (role/persona fixtures via PersonaLoginCache, the plain per-test context in
/// E2ETestBase, and tests that build their own contexts).
///
/// <c>BypassCSP = true</c>: HR.Web enforces a strict Content-Security-Policy with no
/// 'unsafe-eval' (HrWebContentSecurityPolicy — deliberately, and verified by its own unit/pipeline
/// tests). Playwright's string-expression helpers (<c>Page.WaitForFunctionAsync(string)</c>, used
/// throughout the page objects) evaluate their argument with eval inside the page, which that CSP
/// blocks ("EvalError: Evaluating a string as JavaScript violates the following Content Security
/// Policy directive"). Bypassing CSP only in the TEST browser leaves the product policy untouched.
/// No E2E test asserts CSP enforcement/violation reporting; if one is ever added it must create its
/// context with <paramref name="bypassCsp"/> = false.
/// </summary>
public static class E2eBrowserContextOptions
{
    public static BrowserNewContextOptions Create(string? storageState = null, bool bypassCsp = true) =>
        new()
        {
            BypassCSP = bypassCsp,
            StorageState = storageState,
        };
}
