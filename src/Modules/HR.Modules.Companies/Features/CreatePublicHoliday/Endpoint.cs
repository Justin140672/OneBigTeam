using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.CreatePublicHoliday;

internal sealed class Endpoint(
    CreatePublicHolidayHandler handler) : Endpoint<CreatePublicHolidayRequest, CreatePublicHolidayResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/public-holidays");
        Policies("leave:manage");
    }

    public override async Task HandleAsync(
        CreatePublicHolidayRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "conflict")
            {
                await Send.ResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/public-holidays/{result.Value.Id}",
            result.Value));
    }
}
