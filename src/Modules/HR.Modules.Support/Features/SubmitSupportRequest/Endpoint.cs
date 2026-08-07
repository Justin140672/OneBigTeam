using System.Security.Claims;
using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.SubmitSupportRequest;

internal sealed class Endpoint(SubmitSupportRequestHandler handler, ICurrentUser currentUser)
    : Endpoint<SubmitSupportRequestRequest, SubmitSupportRequestResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/support/requests");
        Policies("support:manage");
        AllowFileUploads();
    }

    public override async Task HandleAsync(SubmitSupportRequestRequest request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Verify the caller belongs to the company in the route — never trust the route value
        // alone. Reads the DB-resolved tenant via ICurrentUser, not a raw "company_id" JWT claim —
        // real Supabase-issued tokens never carry one, so relying on the claim directly would
        // Forbid every request unconditionally (see TenantRouteAuthorizationMiddleware).
        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        // ApplicationUser.Id == EmployeeId by convention elsewhere in the platform (see Identity module).
        var result = await handler.HandleAsync(request, userId, userId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: StatusCodes.Status409Conflict));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/support/requests/{result.Value!.Id}", result.Value));
    }
}
