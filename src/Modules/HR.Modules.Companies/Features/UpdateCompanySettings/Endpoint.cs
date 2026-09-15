using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.UpdateCompanySettings;

internal sealed class Endpoint(
	UpdateCompanySettingsHandler handler) : Endpoint<UpdateCompanySettingsRequest, UpdateCompanySettingsResponse>
{
	public override void Configure()
	{
		Put("/api/companies/{companyId:guid}/settings");
        Policies("company:manage");
	}

	public override async Task HandleAsync(
		UpdateCompanySettingsRequest request,
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
