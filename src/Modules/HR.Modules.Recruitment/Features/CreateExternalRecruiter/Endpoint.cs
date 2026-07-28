using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.CreateExternalRecruiter;

internal sealed class Endpoint(CreateExternalRecruiterHandler handler)
    : Endpoint<CreateExternalRecruiterRequest, CreateExternalRecruiterResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/external-recruiters");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        CreateExternalRecruiterRequest request,
        CancellationToken cancellationToken)
    {
        // Defence in depth alongside TenantRouteAuthorizationMiddleware — never trust a client-
        // supplied company identifier for a "manage"-policy write. Mirrors CreateVacancy/Endpoint.cs.
        var companyClaim = User.FindFirstValue("company_id");
        if (!Guid.TryParse(companyClaim, out var callerCompanyId) || callerCompanyId != request.CompanyId)
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

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/external-recruiters/{result.Value.Id}",
            result.Value));
    }
}
