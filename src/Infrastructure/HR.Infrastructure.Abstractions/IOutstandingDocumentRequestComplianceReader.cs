namespace HR.Infrastructure.Abstractions;

/// <summary>
/// ADM-02 Compliance Centre: company-wide outstanding (still-requested) employee document requests,
/// owned by HR.Modules.Documents. Distinct from <see cref="IOutstandingDocumentRequestReader"/>,
/// which is single-employee and not due-date aware. The Compliance Centre coordinating query
/// decides overdue vs due-soon vs informational centrally from <see cref="OutstandingDocumentRequestComplianceItem.DueDate"/>.
/// </summary>
public interface IOutstandingDocumentRequestComplianceReader
{
    Task<IReadOnlyList<OutstandingDocumentRequestComplianceItem>> GetOutstandingDocumentRequestsAsync(
        Guid companyId,
        CancellationToken cancellationToken);
}

public sealed record OutstandingDocumentRequestComplianceItem(
    Guid RequestId,
    Guid EmployeeId,
    string DocumentTypeName,
    DateOnly? DueDate,
    bool IsMandatory);
