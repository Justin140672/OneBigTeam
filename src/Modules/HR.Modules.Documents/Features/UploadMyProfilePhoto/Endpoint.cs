using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.UploadMyProfilePhoto;

internal sealed class Endpoint(UploadMyProfilePhotoHandler handler, ICurrentUser currentUser)
    : Endpoint<UploadMyProfilePhotoRequest, UploadMyProfilePhotoResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/me/profile-photo");
        Policies("role:employee");
        AllowFileUploads();
    }

    public override async Task HandleAsync(
        UploadMyProfilePhotoRequest request,
        CancellationToken cancellationToken)
    {
        // Reads the DB-resolved user id via ICurrentUser, not a raw ClaimTypes.NameIdentifier claim
        // — the JWT bearer handler is configured with MapInboundClaims = false (see HR.Api's
        // ConfigureSupabaseJwtBearer), so real Supabase-issued tokens never populate that mapped
        // claim type; relying on it directly would Unauthorized every request unconditionally.
        if (currentUser.UserId is not Guid employeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Verify the caller belongs to the company in the route. Reads the DB-resolved tenant via
        // ICurrentUser, not a raw "company_id" JWT claim — real Supabase-issued tokens never carry
        // one, so relying on the claim directly would Forbid every request unconditionally (see
        // TenantRouteAuthorizationMiddleware).
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, employeeId, cancellationToken);

        if (result.IsFailure)
        {
            var error = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(error));
                return;
            }

            await Send.ResultAsync(TypedResults.UnprocessableEntity(error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
