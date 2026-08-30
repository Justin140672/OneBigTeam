using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

/// <summary>
/// ADM-02 Compliance Centre reader for company-wide expiring/expired employee documents. Mirrors
/// the query in GetExpiringDocuments/Handler.cs but is company-wide, window-configurable and
/// classifies each document type so the Compliance Centre can split "expiring visas" from
/// "expiring certifications". Classification is keyword-based against the company-defined
/// DocumentType name — deliberately kept here in the owning module rather than leaking the concept
/// out to HR.Modules.Reporting.
/// </summary>
internal sealed class ExpiringEmployeeDocumentReader(DocumentsDbContext db) : IExpiringEmployeeDocumentReader
{
    private static readonly string[] ImmigrationKeywords =
    [
        "visa", "passport", "right to work", "right-to-work", "immigration", "brp",
        "biometric residence", "share code", "settled status", "pre-settled", "work permit",
        "sponsorship", "cos ", "certificate of sponsorship"
    ];

    private static readonly string[] CertificationKeywords =
    [
        "certif", "qualif", "licen", "training", "dbs", "first aid", "cscs", "accreditation",
        "membership", "cpd", "registration", "sia badge", "sia licence", "ndaeb"
    ];

    public async Task<IReadOnlyList<ExpiringEmployeeDocumentItem>> GetExpiringEmployeeDocumentsAsync(
        Guid companyId,
        DateOnly asOf,
        int lookaheadDays,
        CancellationToken cancellationToken)
    {
        var threshold = asOf.AddDays(lookaheadDays);

        var rows = await (
            from ed in db.EmployeeDocuments.AsNoTracking()
            join d in db.Documents.AsNoTracking() on ed.DocumentId equals d.Id
            join dt in db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals dt.Id
            where ed.CompanyId == companyId
               && !ed.IsArchived
               && ed.IsLatestVersion
               && ed.ExpiryDate != null
               && ed.ExpiryDate <= threshold
            select new { ed.EmployeeId, d.Title, TypeName = dt.Name, ed.ExpiryDate })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ExpiringEmployeeDocumentItem(
                r.EmployeeId,
                r.Title,
                r.TypeName,
                r.ExpiryDate!.Value,
                Classify(r.TypeName)))
            .OrderBy(r => r.ExpiryDate)
            .ThenBy(r => r.EmployeeId)
            .ToList();
    }

    private static ComplianceDocumentKind Classify(string documentTypeName)
    {
        var name = documentTypeName.ToLowerInvariant();

        if (ImmigrationKeywords.Any(k => name.Contains(k, StringComparison.Ordinal)))
            return ComplianceDocumentKind.Immigration;

        if (CertificationKeywords.Any(k => name.Contains(k, StringComparison.Ordinal)))
            return ComplianceDocumentKind.Certification;

        return ComplianceDocumentKind.Other;
    }
}
