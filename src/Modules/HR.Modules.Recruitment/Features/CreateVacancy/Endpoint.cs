using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.CreateVacancy;

internal sealed class Endpoint(CreateVacancyHandler handler, ICurrentUser currentUser)
    : Endpoint<CreateVacancyRequest, CreateVacancyResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/vacancies");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        CreateVacancyRequest request,
        CancellationToken cancellationToken)
    {
        // Defence in depth alongside TenantRouteAuthorizationMiddleware (which already blocks any
        // request whose route {companyId} doesn't match the caller's resolved tenant): mirrors the
        // same explicit per-endpoint check used throughout the Documents/DataImport modules (e.g.
        // CompleteSharedCompanyDocumentReview/Endpoint.cs) so this "manage"-policy write never trusts
        // a client-supplied company identifier even if the route-level check is ever bypassed or
        // refactored. Reads the DB-resolved tenant via ICurrentUser, not a raw "company_id" JWT
        // claim — real Supabase-issued tokens never carry one, so relying on the claim directly
        // would Forbid every request unconditionally (see TenantRouteAuthorizationMiddleware).
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

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/vacancies/{result.Value.Id}",
            result.Value));
    }
}
