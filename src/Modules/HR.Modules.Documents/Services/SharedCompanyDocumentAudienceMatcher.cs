using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Services;

/// <summary>
/// Answers "is this employee in this document's audience" and "which employees are eligible for
/// this document" against the <see cref="SharedCompanyDocumentAudienceRule"/> table — a document
/// with no rule rows applies to every active employee, and any rule row matching the employee is
/// enough (rules are OR'd together, not ANDed).
/// </summary>
internal sealed class SharedCompanyDocumentAudienceMatcher(
    DocumentsDbContext db,
    IEmployeeAudienceReader audienceReader)
{
    public async Task<bool> IsEmployeeInAudienceAsync(
        Guid companyId, Guid documentId, Guid employeeId, CancellationToken cancellationToken)
    {
        var rules = await db.SharedCompanyDocumentAudienceRules
            .AsNoTracking()
            .Where(r => r.SharedCompanyDocumentId == documentId)
            .ToListAsync(cancellationToken);

        if (rules.Count == 0)
            return true;

        var profile = await audienceReader.GetEmployeeAudienceAsync(companyId, employeeId, cancellationToken);
        return IsInAudience(rules, profile, employeeId);
    }

    /// <summary>
    /// Pure in-memory version of the same match, for callers (e.g. listing published documents
    /// for one employee across many documents) that already have the rules and profile loaded
    /// and want to avoid a DB round trip per document. An Employee rule naming the caller
    /// directly matches even when their department/location/position profile couldn't be
    /// resolved, since it doesn't depend on that lookup.
    /// </summary>
    public static bool IsInAudience(
        IEnumerable<SharedCompanyDocumentAudienceRule> rules, EmployeeAudienceProfile? profile, Guid employeeId)
    {
        var ruleList = rules as IReadOnlyCollection<SharedCompanyDocumentAudienceRule> ?? rules.ToList();

        if (ruleList.Count == 0)
            return true;

        return ruleList.Any(r => r.RuleType switch
        {
            SharedCompanyDocumentAudienceRuleType.Department => profile is not null && profile.DepartmentId == r.TargetId,
            SharedCompanyDocumentAudienceRuleType.Location   => profile is not null && profile.LocationId == r.TargetId,
            SharedCompanyDocumentAudienceRuleType.Position   => profile is not null && profile.PositionProfileId == r.TargetId,
            SharedCompanyDocumentAudienceRuleType.Employee   => employeeId == r.TargetId,
            _ => false,
        });
    }

    public async Task<IReadOnlyList<Guid>> GetEligibleEmployeeIdsAsync(
        Guid companyId, Guid documentId, CancellationToken cancellationToken)
    {
        var (departmentIds, locationIds, positionProfileIds, employeeIds) =
            await GetRuleTargetsByTypeAsync(documentId, cancellationToken);

        return await audienceReader.GetEligibleEmployeeIdsAsync(
            companyId, departmentIds, locationIds, positionProfileIds, employeeIds, cancellationToken);
    }

    public async Task<(List<Guid> DepartmentIds, List<Guid> LocationIds, List<Guid> PositionProfileIds, List<Guid> EmployeeIds)> GetRuleTargetsByTypeAsync(
        Guid documentId, CancellationToken cancellationToken)
    {
        var rules = await db.SharedCompanyDocumentAudienceRules
            .AsNoTracking()
            .Where(r => r.SharedCompanyDocumentId == documentId)
            .ToListAsync(cancellationToken);

        return (
            rules.Where(r => r.RuleType == SharedCompanyDocumentAudienceRuleType.Department).Select(r => r.TargetId).ToList(),
            rules.Where(r => r.RuleType == SharedCompanyDocumentAudienceRuleType.Location).Select(r => r.TargetId).ToList(),
            rules.Where(r => r.RuleType == SharedCompanyDocumentAudienceRuleType.Position).Select(r => r.TargetId).ToList(),
            rules.Where(r => r.RuleType == SharedCompanyDocumentAudienceRuleType.Employee).Select(r => r.TargetId).ToList());
    }
}
