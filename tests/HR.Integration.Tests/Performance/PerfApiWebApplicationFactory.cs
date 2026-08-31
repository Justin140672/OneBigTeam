using HR.Integration.Tests.Infrastructure;

namespace HR.Integration.Tests.Performance;

/// <summary>
/// NFR-02 performance harness host. Identical to <see cref="ApiWebApplicationFactory"/> but kept as
/// a distinct fixture type so the performance tests get their own xUnit collection and their own
/// Postgres testcontainer, fully isolated from the functional integration suite. Query counting is
/// done process-wide via <see cref="QueryCountingInterceptor"/> (a diagnostic-source observer), so
/// no extra service registration is needed here.
/// </summary>
public sealed class PerfApiWebApplicationFactory : ApiWebApplicationFactory
{
}
