using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.CancelPendingProfilePhoto;

internal sealed class Endpoint(CancelPendingProfilePhotoHandler handler, ICurrentUser currentUser)
    : Endpoint<CancelPendingProfilePhotoRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/employees/me/profile-photo/pending");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancelPendingProfilePhotoRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid employeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var idempotencyKey = HttpContext.Request.Headers["Idempotency-Key"].ToString();

        var result = await handler.HandleAsync(
            request with { IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey },
            employeeId,
            cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(error));
                return;
            }

            await Send.ResultAsync(TypedResults.UnprocessableEntity(error));
            return;
        }

        await Send.ResultAsync(TypedResults.NoContent());
    }
}
