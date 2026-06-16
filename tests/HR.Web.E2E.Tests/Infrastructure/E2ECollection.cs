namespace HR.Web.E2E.Tests.Infrastructure;

// One AppFixture (and one Aspire app + Postgres container) shared across every test in the collection.
// Tests within the collection must be sequential — DevPersonaStore is a process-wide singleton.
[CollectionDefinition("E2E", DisableParallelization = true)]
public class E2ECollection : ICollectionFixture<AppFixture> { }
