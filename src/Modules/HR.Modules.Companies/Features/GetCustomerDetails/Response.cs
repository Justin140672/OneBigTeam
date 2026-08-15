using HR.Infrastructure.Abstractions;

namespace HR.Modules.Companies.Features.GetCustomerDetails;

internal sealed record GetCustomerDetailsResponse(
    Guid CompanyId,
    string CompanyName,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string SubscriptionStatus,
    DateTimeOffset? TrialStartedAt,
    DateTimeOffset? TrialExpiresAt,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    bool AdminForcedReadOnly,
    decimal? MonthlyCharge,
    int ActiveEmployeeCount,
    int TotalEmployeeCount,
    long TotalStorageBytes,
    int StorageFileCount,
    CustomerDetailsSettingsDto? Settings);

internal sealed record CustomerDetailsSettingsDto(
    string TimeZone,
    string Locale,
    WorkingDays WorkingDays,
    decimal HoursPerDay,
    int LeaveYearStartMonth,
    decimal DefaultHolidayAllowance,
    int ProbationMonths,
    EmployeeNumberMode EmployeeNumberMode,
    string? EmployeeNumberPrefix,
    int NextEmployeeNumber);
