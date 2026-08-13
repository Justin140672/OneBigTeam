using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using IAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace HR.Modules.Documents.Features.RejectProfilePhoto;

internal sealed class Endpoint(RejectProfilePhotoHandler handler, IAuthorizationService authorizationService, ICurrentUser currentUser)
    : Endpoint<RejectProfilePhotoRequest, RejectProfilePhotoResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/profile-photo/pending/reject");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        RejectProfilePhotoRequest request,
        CancellationToken cancellationToken)
    {
        // Reads the DB-resolved user id via ICurrentUser, not a raw ClaimTypes.NameIdentifier claim
        // — the JWT bearer handler is configured with MapInboundClaims = false (see HR.Api's
        // ConfigureSupabaseJwtBearer), so real Supabase-issued tokens never populate that mapped
        // claim type; relying on it directly would Unauthorized every request unconditionally.
        if (currentUser.UserId is not Guid reviewerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Verify the caller belongs to the company in the route (applies to all callers). Reads
        // the DB-resolved tenant via ICurrentUser, not a raw "company_id" JWT claim — real
        // Supabase-issued tokens never carry one, so relying on the claim directly would Forbid
        // every request unconditionally (see TenantRouteAuthorizationMiddleware).
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var isManagerUpload = (await authorizationService.AuthorizeAsync(User, "employee:manage")).Succeeded;

        if (!isManagerUpload)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, reviewerId, cancellationToken);

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
