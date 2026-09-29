using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetMyTeam;

internal sealed class Endpoint(GetMyTeamHandler handler, ICurrentUser currentUser) : Endpoint<GetMyTeamRequest, GetMyTeamResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/team");
        Policies("role:employee");
    }

    public override async Task HandleAsync(GetMyTeamRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } managerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request.CompanyId, managerId, request.IncludeIndirect, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
