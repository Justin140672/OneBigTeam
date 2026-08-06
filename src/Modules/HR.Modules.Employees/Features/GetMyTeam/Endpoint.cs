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
        // Self-scoped by the caller's own resolved user id (== Employee.Id, same convention as
        // GetMyEmployee / GetMyOnboardingStatus / GetMyProbationStatus) — no role check needed
        // since a non-manager simply gets an empty team back. NOT User.FindFirst("sub"): see
        // GetMyEmployee/Endpoint.cs for why the raw Supabase claim is wrong here.
        if (currentUser.UserId is not { } managerId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request.CompanyId, managerId, request.IncludeIndirect, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
