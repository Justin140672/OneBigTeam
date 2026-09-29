using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace HR.Modules.Companies.Tests.Infrastructure;

internal sealed class NoOpBackgroundJobClient : IBackgroundJobClient
{
    public readonly List<string> EnqueuedJobTypes = [];

    public string Create(Job job, IState state)
    {
        EnqueuedJobTypes.Add(job.Type.Name);
        return Guid.NewGuid().ToString();
    }

    public bool ChangeState(string jobId, IState state, string? expectedState) => true;
}
