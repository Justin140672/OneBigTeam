using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="IHrHeadcountSummaryReader"/> — records the arguments it was
/// called with and returns a pre-configured result.
/// </summary>
internal sealed class FakeHrHeadcountSummaryReader : IHrHeadcountSummaryReader
{
    private readonly HrHeadcountSummaryResult _result;

    public FakeHrHeadcountSummaryReader(HrHeadcountSummaryResult? result = null)
    {
        _result = result ?? new HrHeadcountSummaryResult([], 0, 0, 0, 0, 0m);
    }

    public Guid? LastCompanyId { get; private set; }
    public ReportFilterCriteria? LastFilter { get; private set; }

    public Task<HrHeadcountSummaryResult> GetHeadcountSummaryAsync(
        Guid companyId,
        ReportFilterCriteria filter,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastFilter = filter;

        return Task.FromResult(_result);
    }
}
