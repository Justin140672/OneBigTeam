using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class NoOpBackgroundJobClient : IBackgroundJobClient
{
    public string Create(Job job, IState state) => Guid.NewGuid().ToString();

    public bool ChangeState(string jobId, IState state, string? expectedState) => true;
}
