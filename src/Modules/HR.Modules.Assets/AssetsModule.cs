using FluentValidation;
using HR.Modules.Assets.Features.AcknowledgeAssetAssignment;
using HR.Modules.Assets.Features.CreateAsset;
using HR.Modules.Assets.Features.CreateAssetAssignment;
using HR.Modules.Assets.Features.CreateAssetCategory;
using HR.Modules.Assets.Features.DeactivateAssetCategory;
using HR.Modules.Assets.Features.ListAssetCategories;
using HR.Modules.Assets.Features.GetAsset;
using HR.Modules.Assets.Features.GetAssetAssignment;
using HR.Modules.Assets.Features.ListAssets;
using HR.Modules.Assets.Features.ListAssetAssignments;
using HR.Modules.Assets.Features.ListEmployeeAssets;
using HR.Modules.Assets.Features.RequestAssetReturn;
using HR.Modules.Assets.Features.ReturnAssetAssignment;
using HR.Modules.Assets.Features.RetireAsset;
using HR.Modules.Assets.Features.UpdateAsset;
using HR.Modules.Assets.Features.UpdateAssetCategory;
using HR.Modules.Assets.Domain;
using HR.Modules.Assets.Persistence;
using HR.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Assets;

public static class AssetsModule
{
    public static IServiceCollection AddAssetsModule(
        this IServiceCollection services,
        string connectionString)
    {
        AddFeatureServices(services);

        services.AddDbContext<AssetsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "assets")));

        return services;
    }

    private static void AddFeatureServices(IServiceCollection services)
    {
        services.AddScoped<IAssetAcknowledgementService, AssetAcknowledgementService>();
        services.AddScoped<IAssetReturnService, AssetReturnService>();
        services.AddScoped<CreateAssetCategoryHandler>();
        services.AddScoped<IValidator<CreateAssetCategoryRequest>, CreateAssetCategoryValidator>();
        services.AddScoped<CreateAssetHandler>();
        services.AddScoped<IValidator<CreateAssetRequest>, CreateAssetValidator>();
        services.AddScoped<CreateAssetAssignmentHandler>();
        services.AddScoped<IValidator<CreateAssetAssignmentRequest>, CreateAssetAssignmentValidator>();
        services.AddScoped<ListAssetCategoriesHandler>();
        services.AddScoped<ListAssetsHandler>();
        services.AddScoped<UpdateAssetCategoryHandler>();
        services.AddScoped<DeactivateAssetCategoryHandler>();
        services.AddScoped<GetAssetHandler>();
        services.AddScoped<GetAssetAssignmentHandler>();
        services.AddScoped<UpdateAssetHandler>();
        services.AddScoped<ListAssetAssignmentsHandler>();
        services.AddScoped<ListEmployeeAssetsHandler>();
        services.AddScoped<RetireAssetHandler>();
        services.AddScoped<RequestAssetReturnHandler>();
        services.AddScoped<IValidator<UpdateAssetRequest>, UpdateAssetValidator>();
    }

    public static async Task MigrateAssetsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS assets");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedAssetsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AssetsDbContext>();

        if (await db.Assets.AnyAsync())
            return;

        var now       = DateTimeOffset.UtcNow;
        var companyId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var sarahId   = Guid.Parse("30000000-0000-0000-0000-000000000001"); // Sarah Chen
        var tomId     = Guid.Parse("30000000-0000-0000-0000-000000000004"); // Tom Williams

        // Fixed IDs so E2E tests can reference them directly.
        var categoryId        = Guid.Parse("c0000000-0000-0000-0000-000000000001");
        var tomAssetId        = Guid.Parse("c0000000-0000-0000-0000-000000000002");
        var tomAssignmentId   = Guid.Parse("c0000000-0000-0000-0000-000000000003");
        var sarahAssetId      = Guid.Parse("c0000000-0000-0000-0000-000000000004");
        var sarahAssignmentId = Guid.Parse("c0000000-0000-0000-0000-000000000005");
        var availableAssetId  = Guid.Parse("c0000000-0000-0000-0000-000000000006");

        var category = AssetCategory.Create(categoryId, companyId, "IT Equipment",
            "Laptops, monitors and peripherals", now);
        db.AssetCategories.Add(category);

        // Tom's asset — MacBook Pro
        var tomAsset = Asset.Create(tomAssetId, companyId, "ASSET-0001", categoryId,
            "MacBook Pro 14\"", "Apple", "MacBook Pro 14-inch M3",
            "C02X12345678", purchaseDate: new DateOnly(2024, 3, 1),
            purchasePrice: 2499.00m, now);
        tomAsset.MarkAssigned(now);
        db.Assets.Add(tomAsset);
        db.AssetAssignments.Add(AssetAssignment.Create(tomAssignmentId, companyId,
            tomAssetId, tomId, sarahId, notes: "Issued for remote work", now));

        // Sarah's asset — Dell monitor so she can see the acknowledgement UI when logged in as dev user
        var sarahAsset = Asset.Create(sarahAssetId, companyId, "ASSET-0002", categoryId,
            "Dell UltraSharp 27\"", "Dell", "U2723DE",
            "CN-0ABC123", purchaseDate: new DateOnly(2024, 1, 15),
            purchasePrice: 699.00m, now);
        sarahAsset.MarkAssigned(now);
        db.Assets.Add(sarahAsset);
        db.AssetAssignments.Add(AssetAssignment.Create(sarahAssignmentId, companyId,
            sarahAssetId, sarahId, sarahId, notes: "Home office monitor", now));

        // Available asset — not assigned, so it appears in the "Assign Asset" dialog dropdown.
        var availableAsset = Asset.Create(availableAssetId, companyId, "ASSET-0003", categoryId,
            "Logitech MX Keys", "Logitech", "MX Keys Advanced",
            "LGT-0003", purchaseDate: new DateOnly(2024, 6, 1),
            purchasePrice: 129.00m, now);
        db.Assets.Add(availableAsset);

        await db.SaveChangesAsync();
    }
}
