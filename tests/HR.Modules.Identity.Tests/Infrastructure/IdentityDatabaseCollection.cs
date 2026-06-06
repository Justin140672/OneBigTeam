using HR.Modules.Identity.Tests.Infrastructure;
using Xunit;

namespace HR.Modules.Identity.Tests.Infrastructure;

[CollectionDefinition("IdentityDatabase")]
public sealed class IdentityDatabaseCollection : ICollectionFixture<IdentityDatabaseFixture>;
