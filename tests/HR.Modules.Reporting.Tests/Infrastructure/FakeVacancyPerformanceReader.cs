using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IVacancyPerformanceReader"/> — records the arguments it was
/// called with and returns a pre-configured set of items.
/// </summary>
internal sealed class FakeVacancyPerformanceReader : IVacancyPerformanceReader
{
    private readonly IReadOnlyList<VacancyPerformanceItem> _items;

    public FakeVacancyPerformanceReader(IReadOnlyList<VacancyPerformanceItem> items)
    {
        _items = items;
    }

    public Guid? LastCompanyId { get; private set; }
    public DateOnly? LastStartDate { get; private set; }
    public DateOnly? LastEndDate { get; private set; }
    public bool? LastIsInternal { get; private set; }

    public Task<IReadOnlyList<VacancyPerformanceItem>> GetVacancyPerformanceAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        bool? isInternal,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastStartDate = startDate;
        LastEndDate = endDate;
        LastIsInternal = isInternal;

        return Task.FromResult(_items);
    }
}
