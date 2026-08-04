using HR.Modules.Support.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Support.Features.GetSupportRequest;

internal sealed class GetSupportRequestHandler(SupportDbContext db)
{
    public async Task<Result<GetSupportRequestResponse>> HandleAsync(
        GetSupportRequestRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await db.SupportRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == request.Id && r.CompanyId == request.CompanyId, cancellationToken);

        if (entity is null)
            return Result.Failure<GetSupportRequestResponse>(Error.NotFound("Support request not found."));

        var attachments = await db.SupportAttachments
            .AsNoTracking()
            .Where(a => a.SupportRequestId == entity.Id)
            .OrderBy(a => a.UploadedAt)
            .Select(a => new GetSupportRequestAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.UploadedAt))
            .ToListAsync(cancellationToken);

        var responses = await db.SupportResponses
            .AsNoTracking()
            .Where(r => r.SupportRequestId == entity.Id)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        var responseIds = responses.Select(r => r.Id).ToList();
        var responseAttachments = await db.SupportResponseAttachments
            .AsNoTracking()
            .Where(a => responseIds.Contains(a.SupportResponseId))
            .ToListAsync(cancellationToken);

        var responseDtos = responses
            .Select(r => new GetSupportRequestResponseDto(
                r.Id,
                r.AuthorUserId,
                r.IsStaffResponse,
                r.BodyHtml,
                r.CreatedAt,
                responseAttachments
                    .Where(a => a.SupportResponseId == r.Id)
                    .Select(a => new GetSupportRequestAttachmentDto(a.Id, a.FileName, a.ContentType, 0, a.UploadedAt))
                    .ToList()))
            .ToList();

        return Result.Success(new GetSupportRequestResponse(
            entity.Id,
            entity.ReferenceNumber,
            entity.Type.ToString(),
            entity.Title,
            entity.Description,
            entity.Priority.ToString(),
            entity.Status.ToString(),
            entity.PageUrl,
            entity.Browser,
            entity.AppVersion,
            entity.IncludeDiagnostics,
            entity.DiagnosticsJson,
            entity.CorrelationId,
            entity.CreatedAt,
            entity.UpdatedAt,
            attachments,
            responseDtos));
    }
}
