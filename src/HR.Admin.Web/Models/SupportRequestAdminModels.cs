namespace HR.Admin.Web.Models;

// Mirrors HR.Modules.Support.Features.{ListSupportRequests,GetSupportRequest,UpdateSupportRequestStatus}
// response shapes exactly — same "app-local DTO matching the API contract" convention as
// CustomerDetailsModels.cs / CustomerSupportViewModels.cs. Read-side shapes also mirror HR.Web's
// own Models/SupportModels.cs (kept in sync by hand, since the two apps don't share a project).

public sealed record SupportRequestListItem(
    Guid Id,
    string ReferenceNumber,
    string Type,
    string Title,
    string Priority,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    string? LatestResponseSnippet);

public sealed record SupportRequestAttachment(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAt);

public sealed record SupportRequestResponseItem(
    Guid Id,
    Guid AuthorUserId,
    bool IsStaffResponse,
    string BodyHtml,
    DateTimeOffset CreatedAt,
    List<SupportRequestAttachment> Attachments);

public sealed record SupportRequestDetailModel(
    Guid Id,
    string ReferenceNumber,
    string Type,
    string Title,
    string Description,
    string Priority,
    string Status,
    string? PageUrl,
    string? Browser,
    string? AppVersion,
    bool IncludeDiagnostics,
    string? DiagnosticsJson,
    string? CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    List<SupportRequestAttachment> Attachments,
    List<SupportRequestResponseItem> Responses);

// Write-path request body for PUT /api/companies/{companyId}/support/requests/{id}/status.
// Ticket 15 optimistic concurrency: ExpectedVersion must be the Version last read from the GET
// response — see UpdateSupportRequestStatusRequest.
public sealed record UpdateSupportRequestStatusRequest(
    Guid CompanyId,
    Guid Id,
    string Status,
    int? ExpectedVersion);

public sealed record UpdateSupportRequestStatusResponse(
    Guid Id,
    string Status,
    DateTimeOffset UpdatedAt,
    int Version);

// Distinguishes a 409 (stale ExpectedVersion — show the conflict banner, do not overwrite until
// the user explicitly reloads) from every other failure (network error, 403 not authorised for
// "support:manage", 404, validation, etc. — show a single generic error message), mirroring the
// Ticket 2/15 optimistic-concurrency UX contract used across the platform (see
// HR.Web.Components.Pages.EditSectionBase).
public enum SupportRequestStatusUpdateOutcome
{
    Success,
    Conflict,
    Failed,
}

public sealed record SupportRequestStatusUpdateResult(
    SupportRequestStatusUpdateOutcome Outcome,
    UpdateSupportRequestStatusResponse? Response,
    string? ErrorMessage);

// Ticket 17: read-side outcomes for the platform-support routes. Unlike the earlier null-means-
// "show one generic error" convention used elsewhere in HR.Admin.Web (e.g.
// CustomerDetailsService.GetCustomerDetailsOrNullAsync), the ticket requires the Support queue/
// details pages to show a distinct message for each of: not signed in at all (401), signed in but
// not an enabled platform administrator (403), the company/request not found (404), and a
// transient failure (network error / 5xx / anything else) — so callers must not collapse these.
public enum SupportRequestFetchOutcome
{
    Success,
    Unauthenticated,
    NotAnEnabledPlatformAdministrator,
    NotFound,
    Failed,
}

public sealed record SupportRequestListFetchResult(
    SupportRequestFetchOutcome Outcome,
    List<SupportRequestListItem>? Items);

public sealed record SupportRequestDetailFetchResult(
    SupportRequestFetchOutcome Outcome,
    SupportRequestDetailModel? Detail);

public static class SupportRequestAdminOptions
{
    public static readonly IReadOnlyList<string> Statuses =
        ["Submitted", "UnderReview", "Planned", "WaitingForCustomer", "Resolved", "Closed"];

    public static string TypeLabel(string type) => type switch
    {
        "ReportProblem" => "Report a Problem",
        "RequestFeature" => "Request a Feature",
        "AskQuestion" => "Ask a Question",
        _ => type,
    };
}
