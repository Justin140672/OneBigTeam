using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.UploadCompanyLogo;

internal sealed class Endpoint(
    UploadCompanyLogoHandler handler) : Endpoint<UploadCompanyLogoRequest, UploadCompanyLogoResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/branding/logos/{assetType}");
        Policies("company:manage");
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
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.BadRequest(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
