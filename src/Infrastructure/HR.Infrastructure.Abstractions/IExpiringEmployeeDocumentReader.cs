namespace HR.Infrastructure.Abstractions;

/// <summary>
/// ADM-02 Compliance Centre: company-wide expiring or already-expired employee documents, owned by
/// HR.Modules.Documents. Each row is classified (immigration / right-to-work vs
/// certification / qualification vs other) so the Compliance Centre can present distinct
/// "expiring visas" and "expiring certifications" sections without HR.Modules.Reporting needing to
/// know anything about the Documents schema or its document-type naming.
/// </summary>
public interface IExpiringEmployeeDocumentReader
{
    /// <summary>
    /// Returns every non-archived, latest-version employee document whose expiry date is on or
    /// before <paramref name="asOf"/> plus <paramref name="lookaheadDays"/> (i.e. already expired or
    /// expiring within the window), scoped to <paramref name="companyId"/>.
    /// </summary>
    Task<IReadOnlyList<ExpiringEmployeeDocumentItem>> GetExpiringEmployeeDocumentsAsync(
        Guid companyId,
        DateOnly asOf,
        int lookaheadDays,
        CancellationToken cancellationToken);
}

public enum ComplianceDocumentKind
{
    Immigration,
    Certification,
    Other
}

public sealed record ExpiringEmployeeDocumentItem(
    Guid EmployeeId,
    string DocumentTitle,
    string DocumentTypeName,
    DateOnly ExpiryDate,
    ComplianceDocumentKind Kind);
