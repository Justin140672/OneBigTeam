using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

internal sealed class FakeLeaveSummaryReader : ILeaveSummaryReader
{
    private readonly IReadOnlyList<LeaveSummaryReportRow> _rows;

    public FakeLeaveSummaryReader(IReadOnlyList<LeaveSummaryReportRow> rows)
    {
        _rows = rows;
    }

    public Guid? LastCompanyId { get; private set; }
    public IReadOnlyCollection<Guid>? LastEmployeeIds { get; private set; }
    public int? LastPolicyYear { get; private set; }

    public Task<IReadOnlyList<LeaveSummaryReportRow>> GetLeaveSummaryAsync(
        Guid companyId,
        IReadOnlyCollection<Guid>? employeeIds,
        int policyYear,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastEmployeeIds = employeeIds;
        LastPolicyYear = policyYear;

        return Task.FromResult(_rows);
    }
}
