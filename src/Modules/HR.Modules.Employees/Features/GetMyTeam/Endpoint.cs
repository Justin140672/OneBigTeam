using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetMyTeam;

internal sealed class Endpoint(GetMyTeamHandler handler) : Endpoint<GetMyTeamRequest, GetMyTeamResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/me/team");
        Policies("authenticated");
    }

    public override async Task HandleAsync(GetMyTeamRequest request, CancellationToken cancellationToken)
    {
        // Self-scoped by the caller's own user id (== Employee.Id, same convention as
        // GetMyEmployee / GetMyOnboardingStatus / GetMyProbationStatus) — no role check needed
        // since a non-manager simply gets an empty team back.
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var managerId))
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request.CompanyId, managerId, request.IncludeIndirect, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
