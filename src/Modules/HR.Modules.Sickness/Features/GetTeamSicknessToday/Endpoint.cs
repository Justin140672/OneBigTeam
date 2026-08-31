using FastEndpoints;
using HR.Modules.Sickness.Services;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.GetTeamSicknessToday;

internal sealed class Endpoint(
    GetTeamSicknessTodayHandler handler,
    ICurrentUser currentUser,
    SicknessResourceAuthorizer resourceAuthorizer) : Endpoint<GetTeamSicknessTodayRequest, GetTeamSicknessTodayResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{managerId:guid}/team-sickness-today");
        Policies("sickness:view-team");
    }

    public override async Task HandleAsync(
        GetTeamSicknessTodayRequest request,
        CancellationToken cancellationToken)
    {
        // NOT User.FindFirst("sub") — that's the raw Supabase Auth user id, not this app's resolved
        // Employee/UserId (see GetMyEmployee/Endpoint.cs for the rationale).
        if (currentUser.UserId is not { } callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        // DSH-02: "sickness:view-team" only proves the caller holds the Manager/HR role. The
        // {managerId} route value is browser-supplied and must be authorized against the caller:
        // caller must BE that manager, sit ABOVE them in the reporting hierarchy, or be an HR
        // administrator. Previously this endpoint accepted any manager id for a caller holding the
        // company-wide permission and self-only otherwise (no skip-level manager path).
        if (!await resourceAuthorizer.CanViewManagerTeamAsync(
                request.CompanyId, callerEmployeeId, request.ManagerId, cancellationToken))
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var response = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
