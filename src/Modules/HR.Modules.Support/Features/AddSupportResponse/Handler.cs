using HR.Infrastructure.Abstractions;
using HR.Modules.Support.Domain;
using HR.Modules.Support.Persistence;
using HR.Modules.Support.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Features.AddSupportResponse;

internal sealed class AddSupportResponseHandler(
    SupportDbContext db,
    IClock clock,
    ISupportAttachmentStorageService attachmentStorage,
    ISupportAttachmentValidator attachmentValidator,
    IUploadedFileScanner fileScanner,
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

        // Security review ticket 4 (P1): same reject-before-upload aggregate/per-file policy as
        // SubmitSupportRequestHandler.
        IReadOnlyList<IFormFile> files = request.Files is { Count: > 0 } collection
            ? [.. collection]
            : [];

        var aggregateCheck = attachmentValidator.ValidateAggregate(files.Count, files.Sum(f => f.Length));
        if (aggregateCheck.IsFailure)
            return Result.Failure<AddSupportResponseResponse>(aggregateCheck.Error);

        foreach (var file in files)
        {
            var fileCheck = attachmentValidator.ValidateFile(file.FileName, file.ContentType, file.Length);
            if (fileCheck.IsFailure)
                return Result.Failure<AddSupportResponseResponse>(fileCheck.Error);
        }

        var now = clock.UtcNowOffset();
        var response = SupportResponse.Create(
            Guid.NewGuid(), supportRequest.Id, request.CompanyId, authorUserId, isStaffResponse, request.BodyHtml, now);
        db.SupportResponses.Add(response);

        var uploadedKeys = new List<string>();
        if (files.Count > 0)
        {
            var uploadResult = await UploadValidatedAttachmentsAsync(
                files, request.CompanyId, supportRequest.Id, response.Id, uploadedKeys, cancellationToken);

            if (!uploadResult.IsSuccess)
            {
                await CleanUpUploadedAsync(uploadedKeys, cancellationToken);
                return Result.Failure<AddSupportResponseResponse>(uploadResult.Error);
            }

            foreach (var (file, storageKey) in uploadResult.Value)
            {
                db.SupportResponseAttachments.Add(SupportResponseAttachment.Create(
                    Guid.NewGuid(), response.Id, request.CompanyId, storageKey,
                    Path.GetFileName(file.FileName), file.ContentType, now));
            }
        }

        supportRequest.Touch(now);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await CleanUpUploadedAsync(uploadedKeys, cancellationToken);
            throw;
        }

        if (isStaffResponse)
            await SendCustomerNotificationAsync(supportRequest, now, cancellationToken);

        return Result.Success(new AddSupportResponseResponse(response.Id, response.IsStaffResponse, response.CreatedAt));
    }

    private async Task<Result<List<(IFormFile File, string StorageKey)>>> UploadValidatedAttachmentsAsync(
        IReadOnlyList<IFormFile> files, Guid companyId, Guid supportRequestId, Guid responseId,
        List<string> uploadedKeys, CancellationToken cancellationToken)
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
                $"support/{companyId}/{supportRequestId}/responses/{responseId}", cancellationToken);

            uploadedKeys.Add(storageKey);
            uploaded.Add((file, storageKey));
        }

        return Result.Success(uploaded);
    }

    private async Task CleanUpUploadedAsync(List<string> uploadedKeys, CancellationToken cancellationToken)
    {
        foreach (var key in uploadedKeys)
        {
            try
            {
                await attachmentStorage.DeleteAsync(key, cancellationToken);
            }
            catch
            {
                // Best-effort cleanup — see SubmitSupportRequestHandler's identical rationale.
            }
        }
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
