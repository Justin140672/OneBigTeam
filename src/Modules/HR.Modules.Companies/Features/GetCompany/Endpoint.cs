using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.GetCompany;

internal sealed class Endpoint(
    GetCompanyHandler handler) : Endpoint<GetCompanyRequest, GetCompanyResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{id:guid}");
        Policies("role:employee");
    }

    public override async Task HandleAsync(
        GetCompanyRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
