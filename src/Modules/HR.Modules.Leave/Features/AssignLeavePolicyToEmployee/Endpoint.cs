using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.AssignLeavePolicyToEmployee;

internal sealed class Endpoint(
    AssignLeavePolicyToEmployeeHandler handler) : Endpoint<AssignLeavePolicyToEmployeeRequest, AssignLeavePolicyToEmployeeResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-policy");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        AssignLeavePolicyToEmployeeRequest request,
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
