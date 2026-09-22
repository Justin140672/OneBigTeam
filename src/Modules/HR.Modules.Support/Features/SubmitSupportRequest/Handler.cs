using System.Text.Encodings.Web;
using System.Text.Json;
using HR.Infrastructure.Abstractions;
using Microsoft.AspNetCore.Http;
using HR.Modules.Support.Domain;
using HR.Modules.Support.Persistence;
using HR.Modules.Support.Services;
using HR.SharedKernel;
using HR.SharedKernel.ExecutionContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Support.Features.SubmitSupportRequest;

internal sealed class SubmitSupportRequestHandler(
    SupportDbContext db,
    IClock clock,
    ISupportAttachmentStorageService attachmentStorage,
    ISupportAttachmentValidator attachmentValidator,
    IUploadedFileScanner fileScanner,
    IEmailSender emailSender,
    IConfiguration configuration,
    IExecutionContextAccessor executionContextAccessor,
    ILogger<SubmitSupportRequestHandler> logger)
{
    public async Task<Result<SubmitSupportRequestResponse>> HandleAsync(
        SubmitSupportRequestRequest request,
        Guid userId,
        Guid? employeeId,
        CancellationToken cancellationToken)
    {
        // Security review ticket 4 (P1): validate the whole batch — count, per-file size, allowed
        // extension/content-type, aggregate size — before uploading a single byte anywhere. A
        // request that violates any limit is rejected in full; nothing is ever partially uploaded.
        IReadOnlyList<IFormFile> files = request.Files is { Count: > 0 } collection
            ? [.. collection]
            : [];
        var aggregateCheck = attachmentValidator.ValidateAggregate(files.Count, files.Sum(f => f.Length));
        if (aggregateCheck.IsFailure)
            return Result.Failure<SubmitSupportRequestResponse>(aggregateCheck.Error);

        foreach (var file in files)
        {
            var fileCheck = attachmentValidator.ValidateFile(file.FileName, file.ContentType, file.Length);
            if (fileCheck.IsFailure)
                return Result.Failure<SubmitSupportRequestResponse>(fileCheck.Error);
        }

        var now = clock.UtcNowOffset();
        var referenceNumber = await GenerateUniqueReferenceNumberAsync(now, cancellationToken);

        string? diagnosticsJson = null;
        if (request.IncludeDiagnostics)
        {
            diagnosticsJson = JsonSerializer.Serialize(new
            {
                request.PageUrl,
                request.Browser,
                request.AppVersion,
                CompanyId = request.CompanyId,
                UserId = userId,
                request.CorrelationId,
                RecentClientErrors = request.RecentClientErrors ?? []
            });
        }

        var entity = SupportRequest.Create(
            Guid.NewGuid(),
            request.CompanyId,
            userId,
            employeeId,
            request.Type,
            request.Title,
            request.Description,
            request.Priority,
            referenceNumber,
            request.PageUrl,
            request.Browser,
            request.AppVersion,
            request.IncludeDiagnostics,
            diagnosticsJson,
            request.CorrelationId,
            now);

        db.SupportRequests.Add(entity);

        // Reliability review issue 4 (P1): every storage key acquired below is tracked in this
        // ownership scope. Commit() is only called after persistence genuinely succeeds — any
        // other exit (an early Result.Failure return, or an exception thrown from the copy/scan/
        // upload/persist sequence, including a second file's upload failing) reaches
        // DisposeAsync without a commit, guaranteeing cleanup regardless of which step failed.
        await using var cleanupScope = new UploadedAttachmentCleanupScope(
            attachmentStorage, db, clock, executionContextAccessor, logger);

        if (files.Count > 0)
        {
            var uploadResult = await UploadValidatedAttachmentsAsync(
                files, request.CompanyId, entity.Id, cleanupScope, cancellationToken);

            if (!uploadResult.IsSuccess)
                return Result.Failure<SubmitSupportRequestResponse>(uploadResult.Error);

            foreach (var (file, storageKey) in uploadResult.Value)
            {
                db.SupportAttachments.Add(SupportAttachment.Create(
                    Guid.NewGuid(), entity.Id, request.CompanyId, storageKey,
                    Path.GetFileName(file.FileName), file.ContentType, file.Length, userId, now));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        cleanupScope.Commit();

        await SendAdminNotificationAsync(entity, now, cancellationToken);

        return Result.Success(new SubmitSupportRequestResponse(entity.Id, entity.ReferenceNumber));
    }

    private async Task SendAdminNotificationAsync(SupportRequest entity, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var adminEmail = configuration["Support:AdminNotificationEmail"];
        var attempt = SupportNotificationAttempt.Create(
            Guid.NewGuid(), entity.Id, entity.CompanyId,
            SupportNotificationType.NewRequestAdminAlert,
            adminEmail ?? string.Empty, now);

        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            attempt.MarkFailed("Support:AdminNotificationEmail is not configured; notification skipped.", now);
        }
        else
        {
            try
            {
                var link = BuildAdminRequestLink(configuration["Support:AdminBaseUrl"], entity.Id);
                await emailSender.SendAsync(
                    adminEmail,
                    $"New support request: {entity.ReferenceNumber}",
                    BuildEmailHtml(entity, link),
                    cancellationToken);
                attempt.MarkSent(clock.UtcNowOffset());
            }
            catch (Exception ex)
            {
                attempt.MarkFailed(ex.Message, clock.UtcNowOffset());
            }
        }

        db.SupportNotificationAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Security review ticket 4 (P1): signature-checks and virus-scans each file in memory before
    /// it is ever written to storage, then uploads it under a random (never filename-derived)
    /// storage key. Aborts the moment any file fails signature verification, scanning, or upload —
    /// an infected/unscannable file in a multi-file request means nothing from that request is
    /// exposed. Reliability review issue 4 (P1): every uploaded key is tracked in
    /// <paramref name="cleanupScope"/> immediately after upload, so a later file in the same batch
    /// throwing (stream-copy error, upload transport error) or failing validation/scan still
    /// results in the earlier files being cleaned up — the caller never needs its own try/catch for
    /// this.
    /// </summary>
    private async Task<Result<List<(IFormFile File, string StorageKey)>>> UploadValidatedAttachmentsAsync(
        IReadOnlyList<IFormFile> files, Guid companyId, Guid supportRequestId,
        UploadedAttachmentCleanupScope cleanupScope, CancellationToken cancellationToken)
    {
        var uploaded = new List<(IFormFile File, string StorageKey)>();

        foreach (var file in files)
        {
            using var buffer = new MemoryStream();
            await using (var openStream = file.OpenReadStream())
            {
                await openStream.CopyToAsync(buffer, cancellationToken);
            }
            buffer.Position = 0;

            var signatureCheck = attachmentValidator.ValidateContentSignature(buffer, file.ContentType);
            if (signatureCheck.IsFailure)
                return Result.Failure<List<(IFormFile, string)>>(signatureCheck.Error);

            UploadedFileScanResult scanResult;
            try
            {
                scanResult = await fileScanner.ScanAsync(buffer, file.FileName, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Reliability review issue 4 (P1): the caller's own cancellation must propagate as
                // OperationCanceledException, not be swallowed into a generic "scan failed" Result
                // — callers (and ASP.NET Core's request pipeline) rely on that type to distinguish
                // "client went away" from a genuine business failure.
                throw;
            }
            catch
            {
                // Fail closed: an unreachable/errored scanner must never let a file through
                // unscanned (mirrors HR.Modules.Documents' ScanUploadedFileJob failure handling).
                return Result.Failure<List<(IFormFile, string)>>(
                    Error.Validation("Attachment scanning is temporarily unavailable. Please try again shortly."));
            }

            if (!scanResult.IsClean)
            {
                return Result.Failure<List<(IFormFile, string)>>(
                    Error.Validation("One or more attached files failed a security scan and were rejected."));
            }

            buffer.Position = 0;
            var storageKey = await attachmentStorage.UploadAsync(
                buffer, file.FileName, file.ContentType,
                $"support/{companyId}/{supportRequestId}", cancellationToken);

            cleanupScope.Track(storageKey);
            uploaded.Add((file, storageKey));
        }

        return Result.Success(uploaded);
    }

    private async Task<string> GenerateUniqueReferenceNumberAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = $"SUP-{now.Year}-{Random.Shared.Next(0, 1_000_000):D6}";
            var exists = await db.SupportRequests.AnyAsync(r => r.ReferenceNumber == candidate, cancellationToken);
            if (!exists)
                return candidate;
        }

        // Extremely unlikely fallback — guarantees uniqueness via a GUID suffix.
        return $"SUP-{now.Year}-{Guid.NewGuid():N}"[..20];
    }

    /// <summary>
    /// Builds the "view request" link from a trusted, configured base URI plus fixed path
    /// segments, validating the scheme before it is ever HTML-encoded for the anchor's <c>href</c>
    /// attribute. <paramref name="requestId"/> is a server-generated GUID, but is included via
    /// <see cref="Uri"/> composition rather than string concatenation regardless.
    /// </summary>
    private static string? BuildAdminRequestLink(string? configuredBaseUrl, Guid requestId)
    {
        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
            return null;

        if (!Uri.TryCreate(configuredBaseUrl.TrimEnd('/'), UriKind.Absolute, out var baseUri))
            return null;

        // Only ever build a link from an http(s) configured base — never trust/construct a link
        // using a scheme that could execute in a mail client (e.g. javascript:).
        if (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            return null;

        var link = new Uri(baseUri, $"/support/requests/{requestId:D}");
        return link.ToString();
    }

    private static string BuildEmailHtml(SupportRequest entity, string? link)
    {
        // entity.Title is user-controlled free text; HTML-encode it for the text context it is
        // rendered into. Reference/Type/Priority are server-generated/enum values but are encoded
        // too for defence in depth. The link is a trusted, validated absolute http(s) URI (or
        // omitted entirely when not configured) — HTML-encode it for the href attribute context.
        var referenceNumber = HtmlEncoder.Default.Encode(entity.ReferenceNumber);
        var type = HtmlEncoder.Default.Encode(entity.Type.ToString());
        var priority = HtmlEncoder.Default.Encode(entity.Priority.ToString());
        var title = HtmlEncoder.Default.Encode(entity.Title);

        var linkHtml = link is null
            ? string.Empty
            : $"""
              <p style="margin:24px 0">
                <a href="{HtmlEncoder.Default.Encode(link)}" style="background:#0d6efd;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
                  View Request
                </a>
              </p>
              """;

        return $"""
            <html>
            <body style="font-family:sans-serif;max-width:600px;margin:auto;padding:24px">
              <h1>New Support Request</h1>
              <p><strong>Reference:</strong> {referenceNumber}</p>
              <p><strong>Type:</strong> {type}</p>
              <p><strong>Priority:</strong> {priority}</p>
              <p><strong>Title:</strong> {title}</p>
              {linkHtml}
            </body>
            </html>
            """;
    }
}
