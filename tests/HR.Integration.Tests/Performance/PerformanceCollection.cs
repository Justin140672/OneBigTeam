namespace HR.Integration.Tests.Performance;

[CollectionDefinition("Performance", DisableParallelization = true)]
public sealed class PerformanceCollection : ICollectionFixture<PerfApiWebApplicationFactory>
{
}
