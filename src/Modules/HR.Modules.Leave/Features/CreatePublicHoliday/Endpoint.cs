using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Leave.Features.CreatePublicHoliday;

internal sealed class Endpoint(
    CreatePublicHolidayHandler handler) : Endpoint<CreatePublicHolidayRequest, CreatePublicHolidayResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/public-holidays");
        Policies("authenticated");
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
                await SendResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        HttpContext.Response.Headers.Location =
            $"/api/companies/{result.Value!.CompanyId}/public-holidays/{result.Value.Id}";

        await SendAsync(result.Value, StatusCodes.Status201Created, cancellationToken);
    }
}
