using HR.SharedKernel;
using FluentValidation;
using Hangfire;

using HR.Modules.Marketing.Domain;
using HR.Modules.Marketing.Features.CreateMarketingFeature;
using HR.Modules.Marketing.Features.CreateMarketingRoadmapItem;
using HR.Modules.Marketing.Features.GetMarketingContent;
using HR.Modules.Marketing.Features.ListMarketingContent;
using HR.Modules.Marketing.Features.ReorderMarketingFeatures;
using HR.Modules.Marketing.Features.ReorderMarketingRoadmapItems;
using HR.Modules.Marketing.Features.SetMarketingFeaturePublication;
using HR.Modules.Marketing.Features.SetMarketingRoadmapItemPublication;
using HR.Modules.Marketing.Features.UpdateMarketingFeature;
using HR.Modules.Marketing.Features.UpdateMarketingRoadmapItem;
using HR.Modules.Marketing.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Marketing;

public static class MarketingModule
{
    public static IServiceCollection AddMarketingModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<MarketingDbContext>(options =>
            options.UseVersionedAggregates().UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "marketing")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<GetMarketingContentHandler>();
        services.AddScoped<IValidator<GetMarketingContentRequest>, GetMarketingContentValidator>();

        services.AddScoped<ListMarketingContentHandler>();
        services.AddScoped<IValidator<ListMarketingContentRequest>, ListMarketingContentValidator>();

        services.AddScoped<CreateMarketingFeatureHandler>();
        services.AddScoped<IValidator<CreateMarketingFeatureRequest>, CreateMarketingFeatureValidator>();

        services.AddScoped<UpdateMarketingFeatureHandler>();
        services.AddScoped<IValidator<UpdateMarketingFeatureRequest>, UpdateMarketingFeatureValidator>();

        services.AddScoped<SetMarketingFeaturePublicationHandler>();
        services.AddScoped<IValidator<SetMarketingFeaturePublicationRequest>, SetMarketingFeaturePublicationValidator>();

        services.AddScoped<ReorderMarketingFeaturesHandler>();
        services.AddScoped<IValidator<ReorderMarketingFeaturesRequest>, ReorderMarketingFeaturesValidator>();

        services.AddScoped<CreateMarketingRoadmapItemHandler>();
        services.AddScoped<IValidator<CreateMarketingRoadmapItemRequest>, CreateMarketingRoadmapItemValidator>();

        services.AddScoped<UpdateMarketingRoadmapItemHandler>();
        services.AddScoped<IValidator<UpdateMarketingRoadmapItemRequest>, UpdateMarketingRoadmapItemValidator>();

        services.AddScoped<SetMarketingRoadmapItemPublicationHandler>();
        services.AddScoped<IValidator<SetMarketingRoadmapItemPublicationRequest>, SetMarketingRoadmapItemPublicationValidator>();

        services.AddScoped<ReorderMarketingRoadmapItemsHandler>();
        services.AddScoped<IValidator<ReorderMarketingRoadmapItemsRequest>, ReorderMarketingRoadmapItemsValidator>();

        services.AddScoped<Jobs.IdempotencyMaintenanceJob>();
    }

    /// <summary>
    /// Ticket 3 (P1) follow-up item 4: cleans up expired idempotency records for this module.
    /// </summary>
    public static WebApplication UseMarketingRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<Jobs.IdempotencyMaintenanceJob>(
            "marketing-idempotency-maintenance",
            job => job.ExecuteAsync(),
            "*/5 * * * *");
        return app;
    }

    public static async Task MigrateMarketingAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketingDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS marketing");
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// Idempotent seed of the marketing content set. Upserts the <see cref="MarketingProduct"/>
    /// singleton, then inserts any of the seed features (matched by slug) / roadmap items (matched
    /// by stable seed id or title) that are not already present. Upgrades untouched legacy
    /// roadmap seeds once, preserving administrator edits and completion/publication choices.
    /// </summary>
    public static async Task SeedMarketingAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketingDbContext>();

        var now = DateTimeOffset.UtcNow;

        var product = await db.MarketingProducts
            .SingleOrDefaultAsync(p => p.Id == MarketingProduct.SingletonId);

        if (product is null)
        {
            product = MarketingProduct.CreateDefault(now);
            db.MarketingProducts.Add(product);
            await db.SaveChangesAsync();
        }

        var existingSlugs = (await db.MarketingFeatures.Select(f => f.Slug).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < SeedFeatures.Length; index++)
        {
            var seed = SeedFeatures[index];
            if (existingSlugs.Contains(seed.Slug))
            {
                continue;
            }

            var created = MarketingFeature.Create(
                Guid.NewGuid(), product.Id, seed.Slug, seed.IconName, seed.Title, seed.Summary,
                seed.Intro, null, seed.Benefits, null, index, MarketingDeliveryStatus.Available, null, now);

            if (created.IsSuccess)
            {
                created.Value!.Publish(null, now);
                db.MarketingFeatures.Add(created.Value!);
            }
        }

        var existingItems = await db.MarketingRoadmapItems.ToListAsync();
        for (var index = 0; index < PhaseTwoRoadmapCatalog.All.Count; index++)
        {
            var seed = PhaseTwoRoadmapCatalog.All[index];
            var existing = existingItems.FirstOrDefault(r => r.Id == seed.Id)
                ?? existingItems.FirstOrDefault(r => string.Equals(r.Title, seed.Title, StringComparison.OrdinalIgnoreCase))
                ?? existingItems.FirstOrDefault(r => seed.LegacyTitle is not null && r.Title == seed.LegacyTitle);

            if (existing is not null)
            {
                // Upgrade only untouched legacy seed content once. Preserve admin changes,
                // publication choices and completed statuses on every subsequent startup.
                if (seed.LegacyTitle is not null && existing.Title == seed.LegacyTitle
                    && existing.CreatedByUserId is null && existing.UpdatedByUserId is null
                    && existing.DeliveryStatus != MarketingDeliveryStatus.Available)
                {
                    existing.Update(seed.Title, seed.Description, seed.IconName,
                        MarketingDeliveryStatus.ComingSoon, index, null, now);
                }
                continue;
            }

            var created = MarketingRoadmapItem.Create(
                seed.Id, product.Id, seed.Title, seed.Description, seed.IconName,
                MarketingDeliveryStatus.ComingSoon, index, null, now);
            if (created.IsFailure)
                throw new InvalidOperationException(created.Error.Message);

            created.Value!.Publish(null, now);
            db.MarketingRoadmapItems.Add(created.Value);
        }
        await db.SaveChangesAsync();
    }

    private sealed record SeedFeature(
        string Slug,
        string IconName,
        string Title,
        string Summary,
        string Intro,
        IReadOnlyList<string> Benefits);


    // Values copied verbatim from src/HR.Marketing/Services/FeatureCatalog.cs (the current seed
    // source of record). That file is intentionally left in place for now.
    private static readonly SeedFeature[] SeedFeatures =
    [
        new SeedFeature(
            "employee-management",
            "users",
            "Employee Management",
            "Store employee information, documents and employment history in one secure place.",
            "Keep core employee information organised, accessible and consistent as your business grows beyond ad hoc files and scattered records.",
            [
                "Employee records and employment history — stop hunting across spreadsheets and old emails for who someone reports to or when they started",
                "Departments, locations and position profiles — see how your organisation is actually structured, not just a flat employee list",
                "Documents and document requests — know exactly which documents you're missing for each employee instead of chasing them ad hoc",
                "Compensation and employment changes — keep a reliable history of pay and role changes instead of relying on memory or old contracts",
                "Notes and audit history — have a clear record of what happened and when, useful when questions come up later",
                "Employee self-service profile — let employees update their own details, cutting down requests landing in HR's inbox"
            ]),
        new SeedFeature(
            "leave-management",
            "calendar-days",
            "Leave Management",
            "Request, approve and track annual leave without spreadsheets or email.",
            "Bring leave requests, balances and approvals into a shared process so everyone understands who is away and what needs action.",
            [
                "Annual leave requests and approvals — replace the email chain of \"can I take this day off\" with a clear, trackable request",
                "Configurable leave types and allowances — match the policy your business actually runs, not a generic default",
                "Team calendars and leave visibility — know at a glance who's away, so you can plan cover without asking around",
                "Leave balances and adjustments — stop manually tallying days in a spreadsheet that's always slightly out of date",
                "Public holiday support — avoid disputes over bank holidays being miscounted against someone's allowance",
                "Approval workflows and reminders — make sure requests don't sit unanswered in a manager's inbox"
            ]),
        new SeedFeature(
            "sickness-absence",
            "heart-pulse",
            "Sickness & Absence",
            "Record sickness, return-to-work meetings and absence trends.",
            "Track sickness and other absence clearly, so managers can respond with better context and keep records complete without relying on inbox history.",
            [
                "Record sickness and absence events — build a complete, consistent record instead of scattered notes across managers",
                "Return-to-work meetings and notes — make sure every absence gets a proper follow-up, not just the long ones",
                "Fit notes and supporting documents — keep the paperwork attached to the right person, not lost in an inbox",
                "Bradford Factor and absence trends — spot absence patterns before they escalate, rather than noticing only after the fact",
                "Absence reporting and dashboards — see which teams are being affected most, without building the report by hand",
                "Configurable sickness policies — apply the policy your business runs, consistently, across every manager"
            ]),
        new SeedFeature(
            "recruitment",
            "user-plus",
            "Recruitment",
            "Find, track and hire the right people with a simple recruitment workflow.",
            "Give hiring activity a simple home, from open roles to candidate progress, so recruitment does not disappear into personal inboxes.",
            [
                "Create and publish vacancies — get a role in front of candidates without juggling a separate job board account",
                "Track candidates through each stage — always know where each candidate stands, instead of digging through email threads",
                "Manage interviews and hiring decisions — keep interview feedback in one place so decisions aren't lost between people",
                "Convert successful candidates into employees — skip re-entering the same details once someone accepts an offer",
                "Recruitment dashboard and pipeline — see how hiring is progressing across all your open roles at a glance",
                "Configurable recruitment workflow — match the stages your business actually uses to hire"
            ]),
        new SeedFeature(
            "company-documents",
            "folder-open",
            "Company Documents",
            "Share company policies, collect acknowledgements and keep important documents organised.",
            "Store company policies, templates and employee-facing documents where teams can find the right version without searching several folders.",
            [
                "Store policies and company documents — give employees one place to find the current version, not five folders",
                "Request employee acknowledgements — get confirmation that policies have actually been read, not just circulated",
                "Track who has read each document — answer \"has everyone seen this?\" without chasing people individually",
                "Manage document versions — avoid the confusion of someone working from an outdated policy",
                "Review and renewal reminders — get prompted before a policy goes stale instead of finding out too late",
                "Secure document storage — keep sensitive company documents access-controlled rather than emailed around"
            ]),
        new SeedFeature(
            "workflows-reminders",
            "diagram-project",
            "Workflows & Reminders",
            "Automate routine HR tasks with reminders, approvals and scheduled actions.",
            "Turn repeat HR admin into more reliable prompts and follow-ups, helping managers stay on top of tasks that otherwise depend on memory.",
            [
                "Automated HR tasks and reminders — stop routine admin depending on someone remembering to do it",
                "Employee onboarding checklists — make sure every new starter gets the same complete setup, every time",
                "Offboarding workflows — avoid loose ends like access or kit not being handled when someone leaves",
                "Probation review scheduling — never let a probation date quietly pass unreviewed",
                "Approval tasks for managers — give managers a clear queue of what needs their attention, not a buried email",
                "Background notifications and alerts — get flagged to things that need action before they become a problem"
            ]),
        new SeedFeature(
            "reporting",
            "chart-line",
            "Reporting",
            "Turn your HR data into clear reports to support better business decisions.",
            "See useful people information more clearly, so leaders can understand workforce activity without stitching together separate spreadsheets.",
            [
                "Headcount and employee reports — answer basic \"how many people do we have, and where\" questions without a manual count",
                "Leave and absence reporting — understand absence patterns across the business, not just one team at a time",
                "Recruitment activity reports — see how hiring is actually going without asking each hiring manager individually",
                "Workload and HR action reports — spot where HR admin is piling up before it becomes a backlog",
                "Export to Excel for further analysis — take the data further when you need a one-off analysis or board report",
                "Visual dashboards and trends — see the story in the data at a glance, not just a table of numbers"
            ]),
    ];

}
