using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Offboarding.Features.WaiveOffboardingTask;

internal sealed class Endpoint(WaiveOffboardingTaskHandler handler, ICurrentUser currentUser)
    : Endpoint<WaiveOffboardingTaskRequest, WaiveOffboardingTaskResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/offboarding/tasks/{offboardingTaskId:guid}/waive");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(WaiveOffboardingTaskRequest request, CancellationToken cancellationToken)
    {
        // Spec SPEC-OFF-01: mandatory reason, actor resolved server-side from the authenticated
        // caller's own resolved Employee/UserId — never client-supplied — mirroring
        // StartLeavingProcess/Endpoint.cs's identical resolution.
        if (currentUser.UserId is not { } actorEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, actorEmployeeId, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
