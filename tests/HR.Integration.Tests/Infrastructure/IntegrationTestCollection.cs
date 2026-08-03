namespace HR.Integration.Tests.Infrastructure;

// A single shared Postgres Testcontainer for the whole assembly instead of one per test class.
// Safe because AssemblyInfo.cs already disables test parallelization for this project (every test
// class ran strictly sequentially even before this change), and every test seeds its own fresh
// Guid.NewGuid() company/user/employee IDs rather than relying on a fresh database per class — so
// sharing one container changes neither execution order nor test isolation, it just removes ~276
// redundant container boot+migration cycles from a full run.
[CollectionDefinition("Integration")]
public sealed class IntegrationTestCollection : ICollectionFixture<ApiWebApplicationFactory>
{
}
