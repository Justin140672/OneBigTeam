using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Documents.Services;

internal sealed class SharedCompanyDocumentAudienceRuleBuilder(IEmployeeAudienceReader audienceReader)
{
    public async Task<Result<IReadOnlyList<SharedCompanyDocumentAudienceRule>>> BuildAsync(
        Guid companyId,
        Guid sharedCompanyDocumentId,
        IReadOnlyCollection<Guid> departmentIds,
        IReadOnlyCollection<Guid> locationIds,
        IReadOnlyCollection<Guid> positionProfileIds,
        IReadOnlyCollection<Guid> employeeIds,
        CancellationToken cancellationToken)
    {
        var rules = new List<SharedCompanyDocumentAudienceRule>();

        foreach (var id in departmentIds.Distinct())
        {
            if (!await audienceReader.DepartmentExistsAsync(companyId, id, cancellationToken))
                return Result.Failure<IReadOnlyList<SharedCompanyDocumentAudienceRule>>(
                    Error.NotFound($"Department '{id}' was not found."));

            rules.Add(SharedCompanyDocumentAudienceRule.Create(
                Guid.NewGuid(), companyId, sharedCompanyDocumentId, SharedCompanyDocumentAudienceRuleType.Department, id));
        }

        foreach (var id in locationIds.Distinct())
        {
            if (!await audienceReader.LocationExistsAsync(companyId, id, cancellationToken))
                return Result.Failure<IReadOnlyList<SharedCompanyDocumentAudienceRule>>(
                    Error.NotFound($"Location '{id}' was not found."));

            rules.Add(SharedCompanyDocumentAudienceRule.Create(
                Guid.NewGuid(), companyId, sharedCompanyDocumentId, SharedCompanyDocumentAudienceRuleType.Location, id));
        }

        foreach (var id in positionProfileIds.Distinct())
        {
            if (!await audienceReader.PositionProfileExistsAsync(companyId, id, cancellationToken))
                return Result.Failure<IReadOnlyList<SharedCompanyDocumentAudienceRule>>(
                    Error.NotFound($"Position '{id}' was not found."));

            rules.Add(SharedCompanyDocumentAudienceRule.Create(
                Guid.NewGuid(), companyId, sharedCompanyDocumentId, SharedCompanyDocumentAudienceRuleType.Position, id));
        }

        foreach (var id in employeeIds.Distinct())
        {
            if (!await audienceReader.EmployeeExistsAsync(companyId, id, cancellationToken))
                return Result.Failure<IReadOnlyList<SharedCompanyDocumentAudienceRule>>(
                    Error.NotFound($"Employee '{id}' was not found."));

            rules.Add(SharedCompanyDocumentAudienceRule.Create(
                Guid.NewGuid(), companyId, sharedCompanyDocumentId, SharedCompanyDocumentAudienceRuleType.Employee, id));
        }

        return Result.Success<IReadOnlyList<SharedCompanyDocumentAudienceRule>>(rules);
    }
}
