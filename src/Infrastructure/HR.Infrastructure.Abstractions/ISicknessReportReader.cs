namespace HR.Infrastructure.Abstractions;

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
