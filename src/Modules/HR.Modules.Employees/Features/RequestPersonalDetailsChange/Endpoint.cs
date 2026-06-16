using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Employees.Features.RequestPersonalDetailsChange;

internal sealed class Endpoint(RequestPersonalDetailsChangeHandler handler)
    : Endpoint<RequestPersonalDetailsChangeRequest, RequestPersonalDetailsChangeResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/employees/{employeeId:guid}/personal-details-change-requests");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        RequestPersonalDetailsChangeRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId))
        {
            await SendResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, userId, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await SendResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            if (result.Error.Code == "forbidden")
            {
                await SendResultAsync(TypedResults.Forbid());
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await SendAsync(result.Value!, StatusCodes.Status201Created, cancellationToken);
    }
}
