using FluentValidation;
using Hangfire;
using HR.Modules.Support.Domain;
using HR.Modules.Support.Features.AddSupportResponse;
using HR.Modules.Support.Features.GetSupportDashboard;
using HR.Modules.Support.Features.GetSupportRequest;
using HR.Modules.Support.Features.ListSupportRequests;
using HR.Modules.Support.Features.SubmitSupportRequest;
using HR.Modules.Support.Features.UpdateSupportRequestStatus;
using HR.Modules.Support.Jobs;
using HR.Modules.Support.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Support;

public static class SupportModule
{
    public static IServiceCollection AddSupportModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<SupportDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "support")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<SubmitSupportRequestHandler>();
        services.AddScoped<IValidator<SubmitSupportRequestRequest>, SubmitSupportRequestValidator>();
        services.AddScoped<ListSupportRequestsHandler>();
        services.AddScoped<GetSupportRequestHandler>();
        services.AddScoped<UpdateSupportRequestStatusHandler>();
        services.AddScoped<IValidator<UpdateSupportRequestStatusRequest>, UpdateSupportRequestStatusValidator>();
        services.AddScoped<AddSupportResponseHandler>();
        services.AddScoped<IValidator<AddSupportResponseRequest>, AddSupportResponseValidator>();
        services.AddScoped<GetSupportDashboardHandler>();
        services.AddScoped<SupportNotificationRetryJob>();
    }

    public static WebApplication UseSupportRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<SupportNotificationRetryJob>(
            "support-notification-retries",
            job => job.ExecuteAsync(),
            Cron.Hourly());
        return app;
    }

    public static async Task MigrateSupportAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS support");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedSupportAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SupportDbContext>();

        if (await db.SupportRequests.AnyAsync())
            return;

        var now = DateTimeOffset.UtcNow;
        var companyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var sarahId = Guid.Parse("30000000-0000-0000-0000-000000000001"); // Sarah Chen

        var request1Id = Guid.Parse("d0000000-0000-0000-0000-000000000001");
        var request2Id = Guid.Parse("d0000000-0000-0000-0000-000000000002");
        var request3Id = Guid.Parse("d0000000-0000-0000-0000-000000000003");

        var request1 = SupportRequest.Create(
            request1Id, companyId, sarahId, sarahId,
            SupportRequestType.ReportProblem,
            "Leave balance not updating after approval",
            "When I approve a leave request the employee's remaining balance still shows the old figure until I refresh the page.",
            SupportRequestPriority.Medium,
            "SUP-2026-000101",
            "/leave/requests",
            "Chrome 128",
            "1.4.2",
            includeDiagnostics: true,
            diagnosticsJson: null,
            correlationId: null,
            now);
        db.SupportRequests.Add(request1);

        var request2 = SupportRequest.Create(
            request2Id, companyId, sarahId, sarahId,
            SupportRequestType.RequestFeature,
            "Bulk export of employee documents",
            "It would help to export all documents for a department in one zip file rather than one at a time.",
            SupportRequestPriority.Low,
            "SUP-2026-000102",
            "/documents",
            "Chrome 128",
            "1.4.2",
            includeDiagnostics: false,
            diagnosticsJson: null,
            correlationId: null,
            now);
        db.SupportRequests.Add(request2);

        var request3 = SupportRequest.Create(
            request3Id, companyId, sarahId, sarahId,
            SupportRequestType.AskQuestion,
            "How do I change a probation review date?",
            "I can't find where to edit the scheduled probation review date for an employee.",
            SupportRequestPriority.Low,
            "SUP-2026-000103",
            "/probation",
            "Chrome 128",
            "1.4.2",
            includeDiagnostics: true,
            diagnosticsJson: null,
            correlationId: null,
            now);
        request3.ChangeStatus(SupportRequestStatus.Resolved, now);
        db.SupportRequests.Add(request3);

        await db.SaveChangesAsync();
    }
}
