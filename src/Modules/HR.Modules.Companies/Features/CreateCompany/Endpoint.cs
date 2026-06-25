using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.CreateCompany;

internal sealed class Endpoint(
    CreateCompanyHandler handler) : Endpoint<CreateCompanyRequest, CreateCompanyResponse>
{
    public override void Configure()
    {
        Post("/api/companies");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        CreateCompanyRequest request,
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

        await Send.ResultAsync(TypedResults.Created($"/api/companies/{result.Value!.Id}", result.Value));
    }
}
