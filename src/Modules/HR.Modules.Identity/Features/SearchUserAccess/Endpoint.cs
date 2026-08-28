using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.SearchUserAccess;

// IAM-08: administrator-facing search across employee, direct role, inherited (position) role and
// override state — same "users:view" gate GetUserAuditHistory/ListUsers already use, since this is
// read-only administrative visibility, not a mutation.
internal sealed class Endpoint(SearchUserAccessHandler handler) : Endpoint<SearchUserAccessRequest, SearchUserAccessResponse>
{
    public override void Configure()
    {
        Get("/api/companies/{companyId:guid}/users/access-search");
        Policies("users:view");
    }

    public override async Task HandleAsync(SearchUserAccessRequest request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request, cancellationToken);
        await Send.ResultAsync(TypedResults.Ok(result));
    }
}
