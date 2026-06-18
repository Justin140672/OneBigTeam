using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.DeleteEmployeeDocument;

internal sealed class Endpoint(DeleteEmployeeDocumentHandler handler)
    : Endpoint<DeleteEmployeeDocumentRequest>
{
    public override void Configure()
    {
        Delete("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents/{employeeDocumentId:guid}");
        Policies("employee:manage");
    }

    public override async Task HandleAsync(
        DeleteEmployeeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await SendResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await SendNoContentAsync(cancellationToken);
    }
}
