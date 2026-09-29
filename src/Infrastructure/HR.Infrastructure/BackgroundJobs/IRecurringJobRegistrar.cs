using Hangfire;

namespace HR.Infrastructure.BackgroundJobs;

public interface IRecurringJobRegistrar
{
    void Register(IRecurringJobManager manager);
}
