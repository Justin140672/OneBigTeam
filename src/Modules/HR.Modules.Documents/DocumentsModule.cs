using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Documents;

public static class DocumentsModule
{
    public static IServiceCollection AddDocumentsModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<DocumentsDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "documents")));

        return services;
    }

    public static async Task MigrateDocumentsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS documents");
        await db.Database.MigrateAsync();
    }

    public static async Task SeedDocumentsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db  = scope.ServiceProvider.GetRequiredService<DocumentsDbContext>();
        var now = DateTimeOffset.UtcNow;

        // ── Acme Corporation ─────────────────────────────────────────────────
        var acmeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        if (!await db.DocumentTypes.AnyAsync(dt => dt.CompanyId == acmeId))
        {
            db.DocumentTypes.AddRange(
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000001"), acmeId, "Contract",           "Employment and service contracts",    now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000002"), acmeId, "Offer Letter",       "Job offer letters",                   now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000003"), acmeId, "Policy",             "Company policies and procedures",     now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000004"), acmeId, "Certificate",        "Training and professional certificates", now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000005"), acmeId, "Pay Slip",           "Monthly pay slips",                   now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000006"), acmeId, "Identity Document",  "Passport, driving licence, etc.",     now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000007"), acmeId, "Performance Review", "Annual and mid-year reviews",         now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000008"), acmeId, "Training Record",    "Completed training records",          now));

            await db.SaveChangesAsync();
        }

        // ── Beta Corp ─────────────────────────────────────────────────────────
        var betaCorpId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        if (!await db.DocumentTypes.AnyAsync(dt => dt.CompanyId == betaCorpId))
        {
            db.DocumentTypes.AddRange(
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000011"), betaCorpId, "Contract",     "Employment and service contracts",  now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000012"), betaCorpId, "Offer Letter", "Job offer letters",                 now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000013"), betaCorpId, "Policy",       "Company policies and procedures",   now));

            await db.SaveChangesAsync();
        }
    }
}
