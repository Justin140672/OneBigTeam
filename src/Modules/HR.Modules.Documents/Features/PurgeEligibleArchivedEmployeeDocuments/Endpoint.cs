using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Documents.Features.PurgeEligibleArchivedEmployeeDocuments;

/// <summary>
/// DOC-04: "a separately authorised retention process can permanently purge eligible files" —
/// gated by "role:company-administrator", a distinctly stronger/narrower boundary than
/// "employee:manage" (which HR Administrators hold and use for every other document
/// archive/restore/manage operation in this module). This is a real, unrecoverable data-
/// destruction capability, so it is an explicit HR/company-admin-triggered endpoint rather than a
/// silently scheduled recurring job — see PurgeEligibleArchivedEmployeeDocumentsHandler's remarks
/// and DocumentsModule.UseDocumentsRecurringJobs, which deliberately does not register this job.
/// </summary>
internal sealed class Endpoint(
    PurgeEligibleArchivedEmployeeDocumentsHandler handler,
    ICurrentUser currentUser) : Endpoint<PurgeEligibleArchivedEmployeeDocumentsRequest, PurgeEligibleArchivedEmployeeDocumentsResponse>
{
    public override void Configure()
    {
        Post("/api/companies/{companyId:guid}/documents/archived/purge-eligible");
        Policies("role:company-administrator");
    }

    public override async Task HandleAsync(
        PurgeEligibleArchivedEmployeeDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid purgedBy)
        {
            await Send.ResultAsync(TypedResults.Unauthorized());
            return;
        }

        if (!Guid.TryParse(currentUser.TenantId, out var callerCompanyId) || callerCompanyId != request.CompanyId)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var result = await handler.HandleAsync(request, purgedBy, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result.Value!));
    }
}
