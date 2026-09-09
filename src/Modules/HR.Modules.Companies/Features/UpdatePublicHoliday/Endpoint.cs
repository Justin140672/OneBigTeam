using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.UpdatePublicHoliday;

internal sealed class Endpoint(
    UpdatePublicHolidayHandler handler) : Endpoint<UpdatePublicHolidayRequest, UpdatePublicHolidayResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/public-holidays/{id:guid}");
        Policies("leave:manage");
    }

    public override async Task HandleAsync(
        UpdatePublicHolidayRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await Send.ResultAsync(ProblemResults.FromError(result.Error));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
