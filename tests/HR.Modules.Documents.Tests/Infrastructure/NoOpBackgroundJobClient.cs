using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace HR.Modules.Documents.Tests.Infrastructure;

/// <summary>
/// No-op Hangfire IBackgroundJobClient for handler unit tests — upload handlers enqueue
/// ScanUploadedFileJob after persisting, but these tests exercise only the synchronous
/// persistence/response behaviour, not the background scan itself (covered separately by
/// ScanUploadedFileJob's own tests). IBackgroundJobClient's real surface is just Create +
/// ChangeState; Enqueue&lt;T&gt;(...) is an extension method built on top of those.
/// </summary>
internal sealed class NoOpBackgroundJobClient : IBackgroundJobClient
{
    public string Create(Job job, IState state) => Guid.NewGuid().ToString();

    public bool ChangeState(string jobId, IState state, string? expectedState) => true;
}
