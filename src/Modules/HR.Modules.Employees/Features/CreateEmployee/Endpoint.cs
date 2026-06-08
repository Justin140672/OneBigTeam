using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.CreateEmployee;

internal sealed class Endpoint(
    CreateEmployeeHandler handler,
    ICurrentUser currentUser,
    IAuthorizationService authorizationService) : Endpoint<CreateEmployeeRequest, CreateEmployeeResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        CreateEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;

        if (userId is null ||
            !await authorizationService.HasPermissionAsync(userId.Value, SystemPermissions.EmployeeCreate, cancellationToken))
        {
            await SendResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await SendResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await SendResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        HttpContext.Response.Headers.Location =
            $"/api/companies/{result.Value!.CompanyId}/employees/{result.Value.Id}";

        await SendAsync(result.Value, StatusCodes.Status201Created, cancellationToken);
    }
}
