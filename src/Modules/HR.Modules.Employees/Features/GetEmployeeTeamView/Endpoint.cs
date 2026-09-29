using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetEmployeeTeamView;

internal sealed class Endpoint(
    GetEmployeeTeamViewHandler handler, ICurrentUser currentUser)
    : Endpoint<GetEmployeeTeamViewRequest, GetEmployeeTeamViewResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{id:guid}/team-view");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetEmployeeTeamViewRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(
            request with { CallerEmployeeId = callerEmployeeId },
            cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
