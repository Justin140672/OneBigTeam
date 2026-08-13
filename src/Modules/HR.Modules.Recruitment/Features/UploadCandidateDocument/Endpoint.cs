using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Recruitment.Features.UploadCandidateDocument;

internal sealed class Endpoint(UploadCandidateDocumentHandler handler, ICurrentUser currentUser)
    : Endpoint<UploadCandidateDocumentRequest, UploadCandidateDocumentResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/candidates/{candidateId:guid}/documents");
        Policies("recruitment:manage");
        AllowFileUploads();
    }

    public override async Task HandleAsync(
        UploadCandidateDocumentRequest request,
        CancellationToken cancellationToken)
    {
        // Reads the DB-resolved user id via ICurrentUser, not a raw ClaimTypes.NameIdentifier claim
        // — the JWT bearer handler is configured with MapInboundClaims = false (see HR.Api's
        // ConfigureSupabaseJwtBearer), so real Supabase-issued tokens never populate that mapped
        // claim type; relying on it directly would Unauthorized every request unconditionally.
        if (currentUser.UserId is not Guid uploadedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        var result = await handler.HandleAsync(request, uploadedBy, cancellationToken);

        if (result.IsFailure)
        {
            var businessError = new { error = result.Error.Message };

            if (result.Error.Code == "not_found")
            {
                await Send.ResultAsync(TypedResults.NotFound(businessError));
                return;
            }

            await Send.ResultAsync(TypedResults.UnprocessableEntity(businessError));
            return;
        }

        await Send.ResultAsync(TypedResults.Created(
            $"/api/companies/{result.Value!.CompanyId}/candidates/{result.Value.CandidateId}/documents/{result.Value.Id}",
            result.Value));
    }
}
