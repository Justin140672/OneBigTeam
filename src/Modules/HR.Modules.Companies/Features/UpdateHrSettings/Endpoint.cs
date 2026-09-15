using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.UpdateHrSettings;

internal sealed class Endpoint(
	UpdateHrSettingsHandler handler) : Endpoint<UpdateHrSettingsRequest, UpdateHrSettingsResponse>
{
	public override void Configure()
	{
		Put("/api/companies/{companyId:guid}/hr-settings");
        Policies("hr-settings:manage");
	}

	public override async Task HandleAsync(
		UpdateHrSettingsRequest request,
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
