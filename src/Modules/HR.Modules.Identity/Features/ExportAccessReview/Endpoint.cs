using FastEndpoints;

namespace HR.Modules.Identity.Features.ExportAccessReview;

internal sealed class Endpoint(ExportAccessReviewHandler handler) : Endpoint<ExportAccessReviewRequest>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/users/access-review/export");
        Policies("users:manage");
    }

    public override async Task HandleAsync(ExportAccessReviewRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        var file = result.File;

        await Send.BytesAsync(
            file.Content,
            fileName: file.FileName,
            contentType: file.ContentType,
            cancellation: cancellationToken);
    }
}
