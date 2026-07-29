using HR.Infrastructure.Abstractions;

namespace HR.Modules.Reporting.Tests.Infrastructure;

/// <summary>
/// Hand-rolled fake for <see cref="ISicknessReportReader"/> — records the arguments it was called
/// with and returns a pre-configured set of records.
/// </summary>
internal sealed class FakeSicknessReportReader : ISicknessReportReader
{
    private readonly IReadOnlyList<SicknessReportRecordItem> _records;

    public FakeSicknessReportReader(IReadOnlyList<SicknessReportRecordItem> records)
    {
        _records = records;
    }

    public Guid? LastCompanyId { get; private set; }
    public DateOnly? LastStartDate { get; private set; }
    public DateOnly? LastEndDate { get; private set; }

    public Task<IReadOnlyList<SicknessReportRecordItem>> GetSicknessRecordsAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken)
    {
        LastCompanyId = companyId;
        LastStartDate = startDate;
        LastEndDate = endDate;

        return Task.FromResult(_records);
    }
}
