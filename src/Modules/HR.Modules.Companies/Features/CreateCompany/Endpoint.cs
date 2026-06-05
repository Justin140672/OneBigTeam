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
                await SendResultAsync(TypedResults.Conflict(businessError));
                return;
            }

            await SendResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        HttpContext.Response.Headers.Location = $"/api/companies/{result.Value!.Id}";
        await SendAsync(result.Value, StatusCodes.Status201Created, cancellationToken);
    }
}
