using FluentValidation;
using HR.Modules.Tasks.Features.CreateTask;
using HR.Modules.Tasks.Features.GetMyTasks;
using HR.Modules.Tasks.Features.GetTask;
using HR.Modules.Tasks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Tasks;

public static class TasksModule
{
    public static IServiceCollection AddTasksModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<TasksDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "tasks")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<CreateTaskHandler>();
        services.AddScoped<IValidator<CreateTaskRequest>, CreateTaskValidator>();
        services.AddScoped<GetTaskHandler>();
        services.AddScoped<GetMyTasksHandler>();
    }

    public static async Task MigrateTasksAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TasksDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS tasks");
        await db.Database.MigrateAsync();
    }
}
