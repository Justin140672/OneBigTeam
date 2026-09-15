using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Companies.Features.UpdateDocumentReminderSettings;

internal sealed class Endpoint(
    UpdateDocumentReminderSettingsHandler handler) : Endpoint<UpdateDocumentReminderSettingsRequest, UpdateDocumentReminderSettingsResponse>
{
    public override void Configure()
    {
        Put("/api/companies/{companyId:guid}/document-reminder-settings");
        Policies("hr-settings:manage");
    }

    public override async Task HandleAsync(
        UpdateDocumentReminderSettingsRequest request,
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
