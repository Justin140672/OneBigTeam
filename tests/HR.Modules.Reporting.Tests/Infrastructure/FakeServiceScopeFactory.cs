using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Reporting.Tests.Infrastructure;

internal sealed class FakeServiceScopeFactory(IReadOnlyList<IWorkloadActionProvider> providers) : IServiceScopeFactory
{
    public IServiceScope CreateScope() => new FakeServiceScope(providers);

    private sealed class FakeServiceScope(IReadOnlyList<IWorkloadActionProvider> providers) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = new FakeServiceProvider(providers);

        public void Dispose()
        {
        }
    }

    private sealed class FakeServiceProvider(IReadOnlyList<IWorkloadActionProvider> providers) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IEnumerable<IWorkloadActionProvider>) ? providers : null;
    }
}
