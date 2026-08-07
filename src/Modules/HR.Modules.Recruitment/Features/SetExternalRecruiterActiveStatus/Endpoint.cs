using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.SetExternalRecruiterActiveStatus;

internal sealed class Endpoint(SetExternalRecruiterActiveStatusHandler handler, ICurrentUser currentUser)
    : Endpoint<SetExternalRecruiterActiveStatusRequest, SetExternalRecruiterActiveStatusResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/external-recruiters/{externalRecruiterId:guid}/active-status");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        SetExternalRecruiterActiveStatusRequest request,
        CancellationToken cancellationToken)
    {
        // Reads the DB-resolved tenant via ICurrentUser, not a raw "company_id" JWT claim — real
        // Supabase-issued tokens never carry one, so relying on the claim directly would Forbid
        // every request unconditionally (see TenantRouteAuthorizationMiddleware).
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.UnprocessableEntity(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
