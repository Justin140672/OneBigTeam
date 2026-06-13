using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.CreateLeavePolicy;

internal sealed class Endpoint(
    CreateLeavePolicyHandler handler) : Endpoint<CreateLeavePolicyRequest, CreateLeavePolicyResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/leave-policies");
        Policies("leave:manage");
    }

    public override async Task HandleAsync(
        CreateLeavePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "conflict")
            {
                await SendResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        HttpContext.Response.Headers.Location =
            $"/api/companies/{result.Value!.CompanyId}/leave-policies/{result.Value.Id}";

        await SendAsync(result.Value, StatusCodes.Status201Created, cancellationToken);
    }
}
