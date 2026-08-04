using System.ComponentModel.DataAnnotations;

namespace HR.Web.Models;

// --- List / detail response shapes (mirror HR.Modules.Support.Features.* Response.cs) ---

public sealed record SupportRequestListItem(
    Guid Id,
    string ReferenceNumber,
    string Type,
    string Title,
    string Priority,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
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
    List<SupportRequestAttachment> Attachments,
    List<SupportRequestResponseItem> Responses);

public sealed record SubmitSupportRequestResult(Guid Id, string ReferenceNumber);

public sealed record UpdateSupportRequestStatusResult(Guid Id, string Status, DateTimeOffset UpdatedAt);

public sealed record AddSupportResponseResult(Guid Id, bool IsStaffResponse, DateTimeOffset CreatedAt);

public sealed record SupportDashboardTitleCount(string Title, int Count);

public sealed record SupportDashboardTypeCount(string Type, int Count);

public sealed record SupportDashboardModel(
    int OpenRequestsCount,
    double? AverageStaffResponseTimeHours,
    List<SupportDashboardTitleCount> TopRequestedFeatures,
    List<SupportDashboardTitleCount> TopReportedProblems,
    List<SupportDashboardTypeCount> RequestsByType);

// --- UI-side option lists (mirror the backend enums; kept as plain strings on the wire since the
// API accepts/returns enum values as strings). ---

public static class SupportRequestOptions
{
    public static readonly IReadOnlyList<string> Types = ["ReportProblem", "RequestFeature", "AskQuestion"];
    public static readonly IReadOnlyList<string> Priorities = ["Low", "Medium", "High"];
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

// --- Submission form model (customer-facing) ---

public sealed class SubmitSupportRequestFormModel
{
    [Required(ErrorMessage = "Type is required.")]
    public string? Type { get; set; }

    [Required(ErrorMessage = "Title is required.")]
    [StringLength(200, ErrorMessage = "Title must be 200 characters or fewer.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Description is required.")]
    [StringLength(4000, ErrorMessage = "Description must be 4000 characters or fewer.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Priority is required.")]
    public string? Priority { get; set; }

    public bool IncludeDiagnostics { get; set; } = true;
}

// --- Reply form model (submission detail / conversation thread) ---

public sealed class AddSupportResponseFormModel
{
    [Required(ErrorMessage = "Reply text is required.")]
    [StringLength(8000, ErrorMessage = "Reply must be 8000 characters or fewer.")]
    public string BodyHtml { get; set; } = string.Empty;
}

// --- Status change form model (admin queue) ---

public sealed class UpdateSupportRequestStatusFormModel
{
    [Required(ErrorMessage = "Status is required.")]
    public string? Status { get; set; }
}
