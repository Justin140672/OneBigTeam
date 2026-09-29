namespace HR.Admin.Web.Models;

public sealed record CustomerDetailsResponse(
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
    CustomerDetailsSettings? Settings);

public sealed record ExtendTrialRequest(DateTimeOffset NewTrialExpiresAt, string Reason);

public sealed record SubscriptionActionRequest(string Reason);

public sealed record GenerateSupportSessionResponse(
    Guid SupportSessionId,
    Guid CompanyId,
    DateTimeOffset ExpiresAt,
    string Token);

public sealed record RevokeSupportSessionResponse(Guid SupportSessionId, DateTimeOffset RevokedAt);

public sealed record CustomerDetailsSettings(
    string TimeZone,
    string Locale,
    string WorkingDays,
    decimal HoursPerDay,
    int LeaveYearStartMonth,
    decimal DefaultHolidayAllowance,
    int ProbationMonths,
    string EmployeeNumberMode,
    string? EmployeeNumberPrefix,
    int NextEmployeeNumber);
