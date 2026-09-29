using FastEndpoints;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Authorization;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetManagerTeamStatusSummary;

internal sealed class Endpoint(
    GetManagerTeamStatusSummaryHandler handler,
    ICurrentUser currentUser,
    IAuthorizationService authorizationService,
    IDirectReportsReader directReportsReader)
    : Endpoint<GetManagerTeamStatusSummaryRequest, GetManagerTeamStatusSummaryResponse>
{
    private static readonly Guid CompanyWidePeopleAccessPermissionId = new("00000000-0000-0000-0001-000000000004");

    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{managerId:guid}/team-status-summary");
        Policies("employee:read");
    }

    public override async Task HandleAsync(
        GetManagerTeamStatusSummaryRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var authorizer = new EmployeeResourceAuthorizer(
            (id, ct) => authorizationService.HasPermissionAsync(id, CompanyWidePeopleAccessPermissionId, ct),
            directReportsReader.GetAllDescendantIdsAsync);

        var allowed = await authorizer.CanAccessAsync(
            request.CompanyId, request.CompanyId, callerEmployeeId, request.ManagerId, cancellationToken);

        if (!allowed)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var response = await handler.HandleAsync(request.CompanyId, request.ManagerId, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(response));
    }
}
