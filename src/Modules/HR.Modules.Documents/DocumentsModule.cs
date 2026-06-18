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
        var acmeId    = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var acmeHrMgr = Guid.Parse("30000000-0000-0000-0000-000000000005"); // Laura Bennett

        if (!await db.DocumentTypes.AnyAsync(dt => dt.CompanyId == acmeId))
        {
            db.DocumentTypes.AddRange(
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000001"), acmeId, "Contract",           "Employment and service contracts",       now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000002"), acmeId, "Offer Letter",       "Job offer letters",                      now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000003"), acmeId, "Policy",             "Company policies and procedures",        now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000004"), acmeId, "Certificate",        "Training and professional certificates", now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000005"), acmeId, "Pay Slip",           "Monthly pay slips",                      now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000006"), acmeId, "Identity Document",  "Passport, driving licence, etc.",        now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000007"), acmeId, "Performance Review", "Annual and mid-year reviews",            now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000008"), acmeId, "Training Record",    "Completed training records",             now));

            await db.SaveChangesAsync();
        }

        if (!await db.Documents.AnyAsync(d => d.CompanyId == acmeId))
        {
            var acmeContractTypeId    = Guid.Parse("50000000-0000-0000-0000-000000000001");
            var acmeOfferLetterTypeId = Guid.Parse("50000000-0000-0000-0000-000000000002");
            var acmePolicyTypeId      = Guid.Parse("50000000-0000-0000-0000-000000000003");

            db.Documents.AddRange(
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000001"), acmeId, null, "Employment Contract – Sarah Chen",   null, acmeContractTypeId,    "employment-contract-sarah-chen.pdf",    184320, "application/pdf", "seed/acme/contracts/employment-contract-sarah-chen.pdf",    null,                       acmeHrMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000002"), acmeId, null, "Employment Contract – James Okafor", null, acmeContractTypeId,    "employment-contract-james-okafor.pdf",  184320, "application/pdf", "seed/acme/contracts/employment-contract-james-okafor.pdf",  null,                       acmeHrMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000003"), acmeId, null, "Employment Contract – Priya Sharma", null, acmeContractTypeId,    "employment-contract-priya-sharma.pdf",  184320, "application/pdf", "seed/acme/contracts/employment-contract-priya-sharma.pdf",  null,                       acmeHrMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000004"), acmeId, null, "Employment Contract – Tom Williams", null, acmeContractTypeId,    "employment-contract-tom-williams.pdf",  184320, "application/pdf", "seed/acme/contracts/employment-contract-tom-williams.pdf",  null,                       acmeHrMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000005"), acmeId, null, "Offer Letter – Tom Williams",        null, acmeOfferLetterTypeId, "offer-letter-tom-williams.pdf",         102400, "application/pdf", "seed/acme/offer-letters/offer-letter-tom-williams.pdf",     null,                       acmeHrMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000006"), acmeId, null, "Employee Handbook 2026",             null, acmePolicyTypeId,      "employee-handbook-2026.pdf",           2097152, "application/pdf", "seed/acme/policies/employee-handbook-2026.pdf",             new DateOnly(2027, 1, 1),   acmeHrMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000007"), acmeId, null, "Remote Working Policy",             null, acmePolicyTypeId,      "remote-working-policy.pdf",             307200, "application/pdf", "seed/acme/policies/remote-working-policy.pdf",              new DateOnly(2027, 6, 30),  acmeHrMgr, now));

            await db.SaveChangesAsync();
        }

        if (!await db.EmployeeDocuments.AnyAsync(ed => ed.CompanyId == acmeId))
        {
            var empSarahId  = Guid.Parse("30000000-0000-0000-0000-000000000001");
            var empJamesId  = Guid.Parse("30000000-0000-0000-0000-000000000002");
            var empPriyaId  = Guid.Parse("30000000-0000-0000-0000-000000000003");
            var empTomId    = Guid.Parse("30000000-0000-0000-0000-000000000004");

            var docSarahContract  = Guid.Parse("60000000-0000-0000-0000-000000000001");
            var docJamesContract  = Guid.Parse("60000000-0000-0000-0000-000000000002");
            var docPriyaContract  = Guid.Parse("60000000-0000-0000-0000-000000000003");
            var docTomContract    = Guid.Parse("60000000-0000-0000-0000-000000000004");
            var docTomOfferLetter = Guid.Parse("60000000-0000-0000-0000-000000000005");
            var docHandbook       = Guid.Parse("60000000-0000-0000-0000-000000000006");
            var docRemotePolicy   = Guid.Parse("60000000-0000-0000-0000-000000000007");

            var sarahContract  = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000001"), acmeId, empSarahId, docSarahContract,  acmeHrMgr, now);
            var sarahHandbook  = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000002"), acmeId, empSarahId, docHandbook,        acmeHrMgr, now);
            var jamesContract  = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000003"), acmeId, empJamesId, docJamesContract,  acmeHrMgr, now);
            var jamesPolicy    = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000004"), acmeId, empJamesId, docRemotePolicy,    acmeHrMgr, now);
            var priyaContract  = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000005"), acmeId, empPriyaId, docPriyaContract,  acmeHrMgr, now);
            var priyaHandbook  = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000006"), acmeId, empPriyaId, docHandbook,        acmeHrMgr, now);
            var tomContract    = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000007"), acmeId, empTomId,   docTomContract,    acmeHrMgr, now);
            var tomOfferLetter = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000008"), acmeId, empTomId,   docTomOfferLetter, acmeHrMgr, now);

            sarahContract.Acknowledge(now);
            sarahHandbook.Acknowledge(now);
            jamesContract.Acknowledge(now);
            priyaContract.Acknowledge(now);
            priyaHandbook.Acknowledge(now);
            tomOfferLetter.Acknowledge(now);

            db.EmployeeDocuments.AddRange(
                sarahContract, sarahHandbook,
                jamesContract, jamesPolicy,
                priyaContract, priyaHandbook,
                tomContract, tomOfferLetter);

            await db.SaveChangesAsync();
        }

        // ── Beta Corp ─────────────────────────────────────────────────────────
        var betaCorpId  = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var betaEngMgr  = Guid.Parse("30000000-0000-0000-0000-000000000011"); // Alice Morgan

        if (!await db.DocumentTypes.AnyAsync(dt => dt.CompanyId == betaCorpId))
        {
            db.DocumentTypes.AddRange(
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000011"), betaCorpId, "Contract",     "Employment and service contracts", now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000012"), betaCorpId, "Offer Letter", "Job offer letters",                now),
                DocumentType.Create(Guid.Parse("50000000-0000-0000-0000-000000000013"), betaCorpId, "Policy",       "Company policies and procedures",  now));

            await db.SaveChangesAsync();
        }

        if (!await db.Documents.AnyAsync(d => d.CompanyId == betaCorpId))
        {
            var betaContractTypeId = Guid.Parse("50000000-0000-0000-0000-000000000011");
            var betaPolicyTypeId   = Guid.Parse("50000000-0000-0000-0000-000000000013");

            db.Documents.AddRange(
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000011"), betaCorpId, null, "Employment Contract – Alice Morgan", null, betaContractTypeId, "employment-contract-alice-morgan.pdf",  184320, "application/pdf", "seed/beta/contracts/employment-contract-alice-morgan.pdf", null,                      betaEngMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000012"), betaCorpId, null, "Employment Contract – Bob Taylor",   null, betaContractTypeId, "employment-contract-bob-taylor.pdf",    184320, "application/pdf", "seed/beta/contracts/employment-contract-bob-taylor.pdf",   null,                      betaEngMgr, now),
                Document.Create(Guid.Parse("60000000-0000-0000-0000-000000000013"), betaCorpId, null, "Employee Handbook 2026",             null, betaPolicyTypeId,   "employee-handbook-2026.pdf",           2097152, "application/pdf", "seed/beta/policies/employee-handbook-2026.pdf",            new DateOnly(2027, 1, 1),  betaEngMgr, now));

            await db.SaveChangesAsync();
        }

        if (!await db.EmployeeDocuments.AnyAsync(ed => ed.CompanyId == betaCorpId))
        {
            var empAliceId = Guid.Parse("30000000-0000-0000-0000-000000000011");
            var empBobId   = Guid.Parse("30000000-0000-0000-0000-000000000012");

            var docAliceContract = Guid.Parse("60000000-0000-0000-0000-000000000011");
            var docBobContract   = Guid.Parse("60000000-0000-0000-0000-000000000012");
            var docBetaHandbook  = Guid.Parse("60000000-0000-0000-0000-000000000013");

            var aliceContract = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000011"), betaCorpId, empAliceId, docAliceContract, betaEngMgr, now);
            var bobContract   = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000012"), betaCorpId, empBobId,   docBobContract,   betaEngMgr, now);
            var bobHandbook   = EmployeeDocument.Create(Guid.Parse("70000000-0000-0000-0000-000000000013"), betaCorpId, empBobId,   docBetaHandbook,  betaEngMgr, now);

            aliceContract.Acknowledge(now);
            bobContract.Acknowledge(now);

            db.EmployeeDocuments.AddRange(aliceContract, bobContract, bobHandbook);

            await db.SaveChangesAsync();
        }
    }
}
