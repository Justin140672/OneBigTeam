using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.UploadCompanyLogo;

internal sealed class Endpoint(
    UploadCompanyLogoHandler handler) : Endpoint<UploadCompanyLogoRequest, UploadCompanyLogoResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{id:guid}/branding/logos/{assetType}");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        UploadCompanyLogoRequest request,
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
