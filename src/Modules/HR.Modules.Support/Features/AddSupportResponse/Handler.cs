using HR.Infrastructure.Abstractions;
using HR.Modules.Support.Domain;
using HR.Modules.Support.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Features.AddSupportResponse;

internal sealed class AddSupportResponseHandler(
    SupportDbContext db,
    IClock clock,
    ISupportAttachmentStorageService attachmentStorage,
    IEmailSender emailSender,
    IUserEmailReader userEmailReader)
{
    public async Task<Result<AddSupportResponseResponse>> HandleAsync(
        AddSupportResponseRequest request,
        Guid authorUserId,
        bool isStaffResponse,
        CancellationToken cancellationToken)
    {
        var supportRequest = await db.SupportRequests
            .SingleOrDefaultAsync(r => r.Id == request.Id && r.CompanyId == request.CompanyId, cancellationToken);

        if (supportRequest is null)
            return Result.Failure<AddSupportResponseResponse>(Error.NotFound("Support request not found."));

        var now = clock.UtcNowOffset();
        var response = SupportResponse.Create(
            Guid.NewGuid(), supportRequest.Id, request.CompanyId, authorUserId, isStaffResponse, request.BodyHtml, now);
        db.SupportResponses.Add(response);

        if (request.Files is { Count: > 0 })
        {
            foreach (var file in request.Files)
            {
                await using var stream = file.OpenReadStream();
                var storageKey = await attachmentStorage.UploadAsync(
                    stream, file.FileName, file.ContentType,
                    $"support/{request.CompanyId}/{supportRequest.Id}/responses/{response.Id}", cancellationToken);

                db.SupportResponseAttachments.Add(SupportResponseAttachment.Create(
                    Guid.NewGuid(), response.Id, request.CompanyId, storageKey, file.FileName, file.ContentType, now));
            }
        }

        supportRequest.Touch(now);
        await db.SaveChangesAsync(cancellationToken);

        if (isStaffResponse)
            await SendCustomerNotificationAsync(supportRequest, now, cancellationToken);

        return Result.Success(new AddSupportResponseResponse(response.Id, response.IsStaffResponse, response.CreatedAt));
    }

    private async Task SendCustomerNotificationAsync(SupportRequest supportRequest, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var recipientEmail = await userEmailReader.GetEmailAsync(
            supportRequest.CompanyId, supportRequest.SubmittedByUserId, cancellationToken);

        var attempt = SupportNotificationAttempt.Create(
            Guid.NewGuid(), supportRequest.Id, supportRequest.CompanyId,
            SupportNotificationType.StaffReplyCustomerNotification,
            recipientEmail ?? string.Empty, now);

        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            attempt.MarkFailed("Could not resolve an email address for the submitting user.", now);
        }
        else
        {
            try
            {
                var subject = $"Update on your support request {supportRequest.ReferenceNumber}";
                var body =
                    $"<p>There's a new reply on your support request <strong>{supportRequest.ReferenceNumber}</strong> — \"{supportRequest.Title}\".</p>" +
                    $"<p>Sign in to view the full conversation and respond.</p>";

                await emailSender.SendAsync(recipientEmail, subject, body, cancellationToken);
                attempt.MarkSent(now);
            }
            catch (Exception ex)
            {
                attempt.MarkFailed(ex.Message, now);
            }
        }

        db.SupportNotificationAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);
    }
}
