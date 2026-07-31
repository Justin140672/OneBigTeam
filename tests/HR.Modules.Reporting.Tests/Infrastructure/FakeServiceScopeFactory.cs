using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Minimal <see cref="IServiceScopeFactory"/> used to unit test GetWorkloadActionsHandler's
/// per-provider-scope parallel invocation (OBT-720) without standing up a real DI container. Every
/// scope it creates resolves the same fixed <see cref="IWorkloadActionProvider"/> set — sufficient
/// for these unit tests since the fakes have no real DbContext to isolate, unlike production
/// providers.
/// </summary>
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
