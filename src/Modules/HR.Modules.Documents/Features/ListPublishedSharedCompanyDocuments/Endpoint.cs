using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.ListPublishedSharedCompanyDocuments;

internal sealed class Endpoint(ListPublishedSharedCompanyDocumentsHandler handler, ICurrentUser currentUser)
    : Endpoint<ListPublishedSharedCompanyDocumentsRequest, ListPublishedSharedCompanyDocumentsResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/shared-documents/published");
        Policies("shared-document:view-published");
    }

    public override async Task HandleAsync(
        ListPublishedSharedCompanyDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid callerEmployeeId)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, callerEmployeeId, cancellationToken);

        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
