using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Offboarding.Features.StartOffboarding;

internal sealed class Endpoint(StartOffboardingHandler handler, ICurrentUser currentUser)
    : Endpoint<StartOffboardingRequest, StartOffboardingResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/offboarding/start");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(StartOffboardingRequest request, CancellationToken cancellationToken)
    {
        // OFF-08: resolved server-side from the authenticated user, never bound from the client
        // body — identifies the human HR actor who manually started this plan.
        var result = await handler.HandleAsync(
            request with { ActorEmployeeId = currentUser.UserId ?? OffboardingSystemActor.Id },
            cancellationToken);

        if (result.IsFailure)
        {
            var statusCode = result.Error.Code == "not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict;
            await Send.ResultAsync(Results.Json(new { error = result.Error.Message }, statusCode: statusCode));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{request.CompanyId}/employees/{request.EmployeeId}/offboarding/{result.Value!.Id}",
            result.Value));
    }
}
