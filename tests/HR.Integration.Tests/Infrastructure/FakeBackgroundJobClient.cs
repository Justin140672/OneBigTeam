using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace HR.Integration.Tests.Infrastructure;

/// <summary>
/// No-op replacement for Hangfire's real <see cref="IBackgroundJobClient"/> used across the
/// integration suite. Several tests (e.g. GetEmployeeProfilePhotoEndpointTests,
/// GetPendingProfilePhotoEndpointTests) explicitly assume enqueued jobs like ScanUploadedFileJob
/// "never actually run inside this integration test" and instead manually drive the entity's scan
/// status to simulate a completed scan. ApiWebApplicationFactory otherwise wires up a real
/// Hangfire server against the Postgres testcontainer (needed so Hangfire's own health check and
/// storage plumbing initialize correctly), so without this override that real BackgroundJobServer
/// races ScanUploadedFileJob's real execution against each test's manual scan-status override —
/// whichever write lands last wins non-deterministically, intermittently flipping an expected
/// "Clean"/Ok response back to "Pending"/"Infected"/NotFound. Swapping in this fake — which simply
/// never creates a job in storage — keeps job enqueuing a deliberate no-op so tests' own manual
/// state setup is the only thing that determines scan status, matching what those tests already
/// assume.
/// </summary>
internal sealed class FakeBackgroundJobClient : IBackgroundJobClient
{
    public string Create(Job job, IState state) => Guid.NewGuid().ToString();

    public bool ChangeState(string jobId, IState state, string? expectedState) => true;
}
