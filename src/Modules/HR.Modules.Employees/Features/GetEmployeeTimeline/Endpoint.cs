using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using IAuthorizationService = Microsoft.AspNetCore.Authorization.IAuthorizationService;

namespace HR.Modules.Employees.Features.GetEmployeeTimeline;

internal sealed class Endpoint(GetEmployeeTimelineHandler handler, IAuthorizationService authorizationService, ICurrentUser currentUser)
    : Endpoint<GetEmployeeTimelineRequest, GetEmployeeTimelineResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/timeline");
        // Broadest applicable policy — any authenticated employee. Visibility filtering (self /
        // manager / HR / none) happens inside the handler per EmployeeTimelineVisibilityResolver,
        // not at this layer, since different callers legitimately see different subsets.
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetEmployeeTimelineRequest request,
        CancellationToken cancellationToken)
    {
        // Reads the DB-resolved user id via ICurrentUser, not a raw ClaimTypes.NameIdentifier claim
        // — the JWT bearer handler is configured with MapInboundClaims = false (see HR.Api's
        // ConfigureSupabaseJwtBearer), so real Supabase-issued tokens never populate that mapped
        // claim type; relying on it directly would Unauthorized every request unconditionally.
        if (currentUser.UserId is not Guid callerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Tenant isolation: never trust the route's companyId alone — verify it matches the
        // caller's DB-resolved tenant via ICurrentUser, not a raw "company_id" JWT claim — real
        // Supabase-issued tokens never carry one, so relying on the claim directly would Forbid
        // every request unconditionally (see TenantRouteAuthorizationMiddleware).
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var callerIsHr = (await authorizationService.AuthorizeAsync(User, "employee:manage")).Succeeded;

        var result = await handler.HandleAsync(request, callerId, callerIsHr, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
