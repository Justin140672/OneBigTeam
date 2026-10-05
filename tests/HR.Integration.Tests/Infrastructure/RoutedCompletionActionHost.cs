using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HR.Modules.Tasks.Contracts;
using HR.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Integration.Tests.Infrastructure;

internal sealed class RoutedCompletionActionHost
{
    private static readonly ConditionalWeakTable<ApiWebApplicationFactory, RoutedCompletionActionHost> Hosts = new();

    private readonly ConcurrentDictionary<Guid, ITaskCompletionAction> _actions = new();

    private RoutedCompletionActionHost(ApiWebApplicationFactory factory)
    {
        Factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<ITaskCompletionAction>(new Router(_actions))));
    }

    public WebApplicationFactory<Program> Factory { get; }

    public static RoutedCompletionActionHost For(ApiWebApplicationFactory factory) =>
        Hosts.GetValue(factory, f => new RoutedCompletionActionHost(f));

    public void Register(Guid taskId, ITaskCompletionAction action) => _actions[taskId] = action;

    private sealed class Router(ConcurrentDictionary<Guid, ITaskCompletionAction> actions) : ITaskCompletionAction
    {
        public TaskSource Source => TaskSource.Workflow;
        public TaskActionType ActionType => TaskActionType.Complete;

        public Task<Result> ExecuteAsync(TaskCompletionContext context, CancellationToken cancellationToken) =>
            actions.TryGetValue(context.TaskId, out var action)
                ? action.ExecuteAsync(context, cancellationToken)
                : Task.FromResult(Result.Success());
    }
}
