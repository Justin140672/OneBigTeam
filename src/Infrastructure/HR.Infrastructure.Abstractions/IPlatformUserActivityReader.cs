namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Provides a platform-wide count of identity.user_profiles rows for the Admin Portal Application
/// Metrics dashboard. NOTE: user_profiles has no last-login/last-active timestamp in this codebase
/// today, so this is a current-count-only snapshot — no historical login-activity trend is
/// available. Implemented in HR.Modules.Identity, consumed by HR.Modules.Companies without a direct
/// module-to-module reference — same cross-module pattern as ICompanyUserCountReader.
/// </summary>
public interface IPlatformUserActivityReader
{
    Task<int> GetTotalUserCountAsync(CancellationToken cancellationToken);
}
