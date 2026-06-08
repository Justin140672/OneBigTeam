using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.GetEmployee;

internal sealed class Endpoint(
    GetEmployeeHandler handler) : Endpoint<GetEmployeeRequest, GetEmployeeResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{id:guid}");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        GetEmployeeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await SendResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
