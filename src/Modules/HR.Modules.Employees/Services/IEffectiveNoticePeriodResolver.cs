using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Employees.Services;

internal interface IEffectiveNoticePeriodResolver
{
    Task<EffectiveNoticePeriod> ResolveAsync(
        Guid companyId,
        NoticePeriodUnit? employeeUnitOverride,
        int? employeeLengthOverride,
        NoticePeriodUnit? positionProfileUnitOverride,
        int? positionProfileLengthOverride,
        CancellationToken cancellationToken);
}
