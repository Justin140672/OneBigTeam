using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.GetMyPermissions;

internal sealed class Endpoint(
    ICurrentUser currentUser,
    IAuthorizationService authorizationService) : EndpointWithoutRequest<GetMyPermissionsResponse>
{
    public override void Configure()
    {
        Get("/api/users/me/permissions");
        Policies("authenticated");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            await SendResultAsync(TypedResults.Forbid());
            return;
        }

        var permissions = await authorizationService.GetEffectivePermissionsAsync(userId.Value, ct);
        await SendAsync(new GetMyPermissionsResponse(permissions.ToList()));
    }
}

internal sealed record GetMyPermissionsResponse(List<Guid> PermissionIds);
