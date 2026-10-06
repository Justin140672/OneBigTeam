using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace HR.Modules.Documents.Tests.Infrastructure;

internal sealed class ThrowingBackgroundJobClient : IBackgroundJobClient
{
    public string Create(Job job, IState state) => throw new InvalidOperationException("Hangfire storage unavailable.");

    public bool ChangeState(string jobId, IState state, string? expectedState) => true;
}
