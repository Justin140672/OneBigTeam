using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.UpdateCompany;

internal sealed class Endpoint(
    UpdateCompanyHandler handler) : Endpoint<UpdateCompanyRequest, UpdateCompanyResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{id:guid}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(
        UpdateCompanyRequest request,
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
