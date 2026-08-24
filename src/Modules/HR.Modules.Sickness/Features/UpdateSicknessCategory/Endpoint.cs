using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Sickness.Features.UpdateSicknessCategory;

internal sealed class Endpoint(UpdateSicknessCategoryHandler handler, ICurrentUser currentUser)
    : Endpoint<UpdateSicknessCategoryRequest, UpdateSicknessCategoryResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/sickness-categories/{id:guid}");
        Policies("sickness:manage");
    }

    public override async Task HandleAsync(UpdateSicknessCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            request with { ActorEmployeeId = currentUser.UserId },
            cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
                return;
            }
            await Send.ResultAsync(TypedResults.Conflict(new { error = result.Error.Message }));
            return;
        }
        await Send.ResultAsync(TypedResults.Ok(result.Value));
    }
}
