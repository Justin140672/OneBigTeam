using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetMyTeamRoster;

internal sealed class Endpoint(GetMyTeamRosterHandler handler, ICurrentUser currentUser)
    : Endpoint<GetMyTeamRosterRequest, GetMyTeamRosterResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/team/roster");
        Policies("role:employee");
    }

    public override async Task HandleAsync(GetMyTeamRosterRequest request, CancellationToken cancellationToken)
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
