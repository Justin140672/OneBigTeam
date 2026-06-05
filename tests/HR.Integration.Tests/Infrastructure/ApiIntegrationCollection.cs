using Xunit;

namespace HR.Integration.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class ApiIntegrationCollection : ICollectionFixture<ApiWebApplicationFactory>
{
    public const string Name = "api-integration";
}