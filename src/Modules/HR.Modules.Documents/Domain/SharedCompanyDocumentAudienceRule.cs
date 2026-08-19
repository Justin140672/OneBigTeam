using HR.Modules.Employees.Contracts;
namespace HR.Modules.Documents.Domain;

/// <summary>
/// One "OR" clause in a document's audience: a document is visible to an employee who matches
/// at least one rule (a department they belong to, a location they belong to, a position they
/// hold, or their own employee id being listed directly). No rows for a document means "all
/// employees" — the absence of rules is itself the "everyone" audience, not a special row, so
/// documents intended for everyone never require per-employee records to be visible. TargetId's
/// meaning depends on RuleType and is never a foreign key here — departments/locations/positions/
/// employees all live in the Employees module, so (like the rest of this cross-module boundary)
/// existence is validated by <c>IEmployeeAudienceReader</c> at write time, not by a DB constraint.
/// </summary>
internal sealed class SharedCompanyDocumentAudienceRule
{
    private SharedCompanyDocumentAudienceRule() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SharedCompanyDocumentId { get; private set; }
    public SharedCompanyDocumentAudienceRuleType RuleType { get; private set; }
    public Guid TargetId { get; private set; }

    public static SharedCompanyDocumentAudienceRule Create(
        Guid id,
        Guid companyId,
        Guid sharedCompanyDocumentId,
        SharedCompanyDocumentAudienceRuleType ruleType,
        Guid targetId) => new()
    {
        Id                      = id,
        CompanyId               = companyId,
        SharedCompanyDocumentId = sharedCompanyDocumentId,
        RuleType                = ruleType,
        TargetId                = targetId,
    };
}
