using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace HR.Modules.Recruitment.Tests.Infrastructure;

internal sealed class RecordingBackgroundJobClient : IBackgroundJobClient
{
    public List<Job> CreatedJobs { get; } = [];

    public List<IState> CreatedStates { get; } = [];

    public string Create(Job job, IState state)
    {
        CreatedJobs.Add(job);
        CreatedStates.Add(state);
        return Guid.NewGuid().ToString();
    }

    public bool ChangeState(string jobId, IState state, string? expectedState) => true;
}
