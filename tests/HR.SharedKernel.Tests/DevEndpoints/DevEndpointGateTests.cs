using HR.SharedKernel.DevEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HR.SharedKernel.Tests.DevEndpoints;

// Toggles the process-wide E2E_TESTING variable, so it must not run in parallel with other tests
// that read it.
[Collection("E2E_TESTING environment variable")]
public sealed class DevEndpointGateTests
{
    [Theory]
    // E2E kind: only Development + E2E_TESTING=true.
    [InlineData("Production", "true", false, DevEndpointKind.E2E, false)]
    [InlineData("Production", null, false, DevEndpointKind.E2E, false)]
    [InlineData("Staging", "true", false, DevEndpointKind.E2E, false)]
    [InlineData("Staging", null, false, DevEndpointKind.E2E, false)]
    [InlineData("Development", null, true, DevEndpointKind.E2E, false)]
    [InlineData("Development", "false", true, DevEndpointKind.E2E, false)]
    [InlineData("Development", "true", false, DevEndpointKind.E2E, true)]
    // DevTools kind: only Development + DevTools:Enabled=true (E2E_TESTING is irrelevant).
    [InlineData("Production", null, true, DevEndpointKind.DevTools, false)]
    [InlineData("Staging", "true", true, DevEndpointKind.DevTools, false)]
    [InlineData("Development", "true", false, DevEndpointKind.DevTools, false)]
    [InlineData("Development", null, true, DevEndpointKind.DevTools, true)]
    public void IsAvailable_Requires_Development_And_The_Kind_Switch(
        string environmentName, string? e2eValue, bool devToolsEnabled, DevEndpointKind kind, bool expected)
    {
        var original = Environment.GetEnvironmentVariable(DevEndpointGate.E2ETestingVariable);
        try
        {
            Environment.SetEnvironmentVariable(DevEndpointGate.E2ETestingVariable, e2eValue);

            var services = new ServiceCollection()
                .AddSingleton<IHostEnvironment>(new FakeEnvironment(environmentName))
                .AddSingleton<IConfiguration>(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["DevTools:Enabled"] = devToolsEnabled.ToString() })
                    .Build())
                .BuildServiceProvider();

            Assert.Equal(expected, DevEndpointGate.IsAvailable(services, kind));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevEndpointGate.E2ETestingVariable, original);
        }
    }

    [Fact]
    public void IsAvailable_Fails_Closed_When_No_Host_Environment_Is_Registered()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        Assert.False(DevEndpointGate.IsAvailable(services, DevEndpointKind.E2E));
        Assert.False(DevEndpointGate.IsAvailable(services, DevEndpointKind.DevTools));
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
