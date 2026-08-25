using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace HR.Modules.Notifications.Tests.Infrastructure;

/// <summary>
/// No-op Hangfire IBackgroundJobClient — mirrors HR.Modules.Documents.Tests' NoOpBackgroundJobClient
/// (same rationale: IBackgroundJobClient's real surface is just Create + ChangeState; Enqueue&lt;T&gt;
/// is an extension method on top). Used by tests that exercise NotificationWriter but don't need to
/// assert on the enqueued EmailDeliveryJob itself (see FakeBackgroundJobClient for tests that do).
/// </summary>
internal sealed class NoOpBackgroundJobClient : IBackgroundJobClient
{
    public string Create(Job job, IState state) => Guid.NewGuid().ToString();

    public bool ChangeState(string jobId, IState state, string? expectedState) => true;
}
