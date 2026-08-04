namespace HR.Modules.Support.Features.GetSupportRequest;

internal sealed record GetSupportRequestResponse(
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
    List<GetSupportRequestAttachmentDto> Attachments,
    List<GetSupportRequestResponseDto> Responses);

internal sealed record GetSupportRequestAttachmentDto(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset UploadedAt);

internal sealed record GetSupportRequestResponseDto(
    Guid Id,
    Guid AuthorUserId,
    bool IsStaffResponse,
    string BodyHtml,
    DateTimeOffset CreatedAt,
    List<GetSupportRequestAttachmentDto> Attachments);
