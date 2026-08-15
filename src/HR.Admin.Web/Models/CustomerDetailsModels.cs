namespace HR.Admin.Web.Models;

// Mirrors HR.Modules.Companies.Features.GetCustomerDetails.Response's shape exactly — same
// "app-local DTO matching the API contract" convention as CustomerListModels.cs and
// CustomerDashboardModels.cs. HR.Admin.Web does not reference HR.Infrastructure.Abstractions
// (or any module project), so the WorkingDays/EmployeeNumberMode enums on the API side are
// modeled here as plain strings and rendered as-is (the API serializes enums to their string
// name by default via JsonStringEnumConverter conventions used elsewhere in this app).
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

// Request/response DTOs for the Subscription Management admin actions (support intervention on a
// customer's subscription). Reason is required on every action — it doubles as proof the admin
// acknowledged the warning shown in the confirmation dialog before submitting, matching the
// acceptance criteria ("display warnings before execution" + "are audited").
public sealed record ExtendTrialRequest(DateTimeOffset NewTrialExpiresAt, string Reason);

public sealed record SubscriptionActionRequest(string Reason);

// Response DTOs for the "Login as customer" support-session action (Support epic). Reuses
// SubscriptionActionRequest(Reason) above as the request body for generating a session — the
// revoke call takes no body, matching the route-bound /support-sessions/{id}/revoke endpoint.
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
