using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.GetEmployeeDocument;

internal sealed class Endpoint(GetEmployeeDocumentHandler handler)
    : Endpoint<GetEmployeeDocumentRequest, GetEmployeeDocumentResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/employees/{employeeId:guid}/documents/{employeeDocumentId:guid}");
        Policies("authenticated");
    }

    public override async Task HandleAsync(
        GetEmployeeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            await SendResultAsync(TypedResults.NotFound(new { error = result.Error.Message }));
            return;
        }

        await SendAsync(result.Value!, StatusCodes.Status200OK, cancellationToken);
    }
}
