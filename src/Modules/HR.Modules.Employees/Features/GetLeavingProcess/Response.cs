using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;

namespace HR.Modules.Employees.Features.GetLeavingProcess;

internal sealed record GetLeavingProcessResponse(
    Guid Id,
    DateOnly ResignationReceivedDate,
    DateOnly LeavingDate,
    DateOnly LastWorkingDay,
    NoticePeriodUnit NoticePeriodUnit,
    int NoticePeriodLength,
    string NoticeSource,
    string LeavingReason,
    string Status);
