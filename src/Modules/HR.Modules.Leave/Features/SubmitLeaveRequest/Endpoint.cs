using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.SubmitLeaveRequest;

internal sealed class Endpoint(
    SubmitLeaveRequestHandler handler) : Endpoint<SubmitLeaveRequestRequest, SubmitLeaveRequestResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/leave-requests");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        SubmitLeaveRequestRequest request,
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

        HttpContext.Response.Headers.Location =
            $"/api/companies/{result.Value!.CompanyId}/employees/{result.Value.EmployeeId}/leave-requests/{result.Value.Id}";

        await SendAsync(result.Value, StatusCodes.Status201Created, cancellationToken);
    }
}
