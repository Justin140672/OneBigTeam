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
using Microsoft.Extensions.DependencyInjection;
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
    IServiceScopeFactory serviceScopeFactory,
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

        await using var cleanupScope = new UploadedAttachmentCleanupScope(
            attachmentStorage, serviceScopeFactory, clock, executionContextAccessor, logger);

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
                var email = SupportEmailRenderer.RenderNewRequestAdminAlert(
                    entity.ReferenceNumber, entity.Type, entity.Priority, entity.Title, link);
                await emailSender.SendAsync(adminEmail, email.Subject, email.HtmlBody, cancellationToken);
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
                throw;
            }
            catch
            {
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

        return $"SUP-{now.Year}-{Guid.NewGuid():N}"[..20];
    }

    private static string? BuildAdminRequestLink(string? configuredBaseUrl, Guid requestId)
    {
        if (string.IsNullOrWhiteSpace(configuredBaseUrl))
            return null;

        if (!Uri.TryCreate(configuredBaseUrl.TrimEnd('/'), UriKind.Absolute, out var baseUri))
            return null;

        if (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps)
            return null;

        var link = new Uri(baseUri, $"/support/requests/{requestId:D}");
        return link.ToString();
    }
}
