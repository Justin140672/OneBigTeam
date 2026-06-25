using Hangfire;

namespace HR.Infrastructure.BackgroundJobs;

/// <summary>
/// Implemented by classes that register Hangfire recurring jobs.
/// Implementations are discovered via DI at startup and invoked once to schedule their jobs.
/// All background job definitions live in HR.Infrastructure — modules participate via integration events.
/// </summary>
public interface IRecurringJobRegistrar
{
    void Register(IRecurringJobManager manager);
}
