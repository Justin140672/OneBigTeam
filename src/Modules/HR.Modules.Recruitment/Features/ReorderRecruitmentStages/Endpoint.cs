using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.ReorderRecruitmentStages;

internal sealed class Endpoint(ReorderRecruitmentStagesHandler handler, ICurrentUser currentUser)
    : Endpoint<ReorderRecruitmentStagesRequest, ReorderRecruitmentStagesResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/recruitment-stages/reorder");
        Policies("recruitment:manage");
    }

    public override async Task HandleAsync(
        ReorderRecruitmentStagesRequest request,
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
            await Send.ResultAsync(TypedResults.UnprocessableEntity(new { error = result.Error.Message }));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
