using HR.Infrastructure.Abstractions;

namespace HR.Modules.Employees.Features.StartLeavingProcess;

internal sealed record StartLeavingProcessResponse(
    Guid Id,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly ResignationReceivedDate,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    NoticePeriodUnit NoticePeriodUnit,
    int NoticePeriodLength,
    string NoticeSource,
    string LeavingReason,
    string Status,
    DateTimeOffset StartedAt);
