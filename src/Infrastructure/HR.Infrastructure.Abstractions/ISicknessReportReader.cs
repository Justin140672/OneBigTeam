namespace HR.Infrastructure.Abstractions;

/// <summary>
/// Company-wide sickness absence data for the Sickness Report (OBT-708), as owned by
/// HR.Modules.Sickness. Returns one row per sickness record overlapping the supplied date range
/// (or all records when no range is supplied) so the Reporting module can group/total by employee
/// or department without a direct module-to-module reference.
/// </summary>
public interface ISicknessReportReader
{
    Task<IReadOnlyList<SicknessReportRecordItem>> GetSicknessRecordsAsync(
        Guid companyId,
        DateOnly? startDate,
        DateOnly? endDate,
        CancellationToken cancellationToken);
}

public sealed record SicknessReportRecordItem(
    Guid EmployeeId,
    Guid RecordId,
    DateOnly StartDate,
    DateOnly? EndDate,
    decimal DaysAbsent);
