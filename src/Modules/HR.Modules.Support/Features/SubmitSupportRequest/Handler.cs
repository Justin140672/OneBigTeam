using System.Text.Encodings.Web;
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
