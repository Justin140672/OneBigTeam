namespace HR.Integration.Tests.Performance;

/// <summary>
/// Dedicated collection for the NFR-02 performance/scale tests. Separate from the "Integration"
/// collection so it gets its own <see cref="PerfApiWebApplicationFactory"/> (with the query-count
/// interceptor) and its own Postgres testcontainer. The assembly already disables test
/// parallelization, so these run strictly after the functional suite.
/// </summary>
[CollectionDefinition("Performance")]
public sealed class PerformanceCollection : ICollectionFixture<PerfApiWebApplicationFactory>
{
}
