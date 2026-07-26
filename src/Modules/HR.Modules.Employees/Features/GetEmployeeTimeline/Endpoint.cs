using System.Security.Claims;
using FastEndpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetEmployeeTimeline;

internal sealed class Endpoint(GetEmployeeTimelineHandler handler, IAuthorizationService authorizationService)
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
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var callerId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // Tenant isolation: never trust the route's companyId alone — verify it matches the
        // caller's own company claim, mirroring GetDocumentRequest / GetEmployeeAcknowledgementHistory.
        var companyClaim = User.FindFirstValue("company_id");
        if (!Guid.TryParse(companyClaim, out var callerCompanyId) || callerCompanyId != request.CompanyId)
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
