using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.GetMyProfilePhoto;

internal sealed class Endpoint(GetMyProfilePhotoHandler handler, ICurrentUser currentUser) : EndpointWithoutRequest<GetMyProfilePhotoResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/profile-photo");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
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

        if (!Guid.TryParse(Route<string>("companyId"), out var companyId))
        {
            await Send.ResultAsync(TypedResults.BadRequest());
            return;
        }

        var response = await handler.HandleAsync(companyId, employeeId, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
