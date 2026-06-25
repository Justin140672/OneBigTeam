using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.CreateDepartment;

internal sealed class Endpoint(
    CreateDepartmentHandler handler) : Endpoint<CreateDepartmentRequest, CreateDepartmentResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/departments");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        CreateDepartmentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/departments/{result.Value.Id}",
            result.Value));
    }
}
