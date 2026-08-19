using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Employees.Services;

namespace HR.Modules.Employees.Tests.Infrastructure;

internal sealed class FakeEffectiveNoticePeriodResolver(EffectiveNoticePeriod? result = null) : IEffectiveNoticePeriodResolver
{
    private static readonly EffectiveNoticePeriod Default = new(NoticePeriodUnit.Months, 1, NoticePeriodSource.CompanyDefault);

    public Task<EffectiveNoticePeriod> ResolveAsync(
        Guid companyId,
        NoticePeriodUnit? employeeUnitOverride,
        int? employeeLengthOverride,
        NoticePeriodUnit? positionProfileUnitOverride,
        int? positionProfileLengthOverride,
        CancellationToken cancellationToken)
        => Task.FromResult(result ?? Default);
}
