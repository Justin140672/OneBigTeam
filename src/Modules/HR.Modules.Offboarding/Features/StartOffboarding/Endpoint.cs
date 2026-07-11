using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Offboarding.Features.StartOffboarding;

internal sealed class Endpoint(StartOffboardingHandler handler)
    : Endpoint<StartOffboardingRequest, StartOffboardingResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/offboarding/start");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(StartOffboardingRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

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
