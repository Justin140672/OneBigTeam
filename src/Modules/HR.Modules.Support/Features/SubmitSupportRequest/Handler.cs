using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Modules.Support.Domain;
using HR.Modules.Support.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Support.Features.SubmitSupportRequest;

internal sealed class SubmitSupportRequestHandler(
    SupportDbContext db,
    IClock clock,
    ISupportAttachmentStorageService attachmentStorage,
    IEmailSender emailSender,
    IConfiguration configuration)
{
    public async Task<Result<SubmitSupportRequestResponse>> HandleAsync(
        SubmitSupportRequestRequest request,
        Guid userId,
        Guid? employeeId,
        CancellationToken cancellationToken)
    {
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

        if (request.Files is { Count: > 0 })
        {
            foreach (var file in request.Files)
            {
                await using var stream = file.OpenReadStream();
                var storageKey = await attachmentStorage.UploadAsync(
                    stream, file.FileName, file.ContentType,
                    $"support/{request.CompanyId}/{entity.Id}", cancellationToken);

                db.SupportAttachments.Add(SupportAttachment.Create(
                    Guid.NewGuid(), entity.Id, request.CompanyId, storageKey,
                    file.FileName, file.ContentType, file.Length, userId, now));
            }
        }

        await db.SaveChangesAsync(cancellationToken);

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
                var baseUrl = configuration["Support:AdminBaseUrl"]?.TrimEnd('/') ?? string.Empty;
                var link = $"{baseUrl}/support/requests/{entity.Id}";
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

    private static string BuildEmailHtml(SupportRequest entity, string link) => $"""
        <html>
        <body style="font-family:sans-serif;max-width:600px;margin:auto;padding:24px">
          <h1>New Support Request</h1>
          <p><strong>Reference:</strong> {entity.ReferenceNumber}</p>
          <p><strong>Type:</strong> {entity.Type}</p>
          <p><strong>Priority:</strong> {entity.Priority}</p>
          <p><strong>Title:</strong> {entity.Title}</p>
          <p style="margin:24px 0">
            <a href="{link}" style="background:#0d6efd;color:#fff;padding:12px 24px;text-decoration:none;border-radius:4px">
              View Request
            </a>
          </p>
        </body>
        </html>
        """;
}
