using FastEndpoints;
using HR.SharedKernel;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Identity.Features.GetMe;

internal sealed class Endpoint(
    ICurrentUser currentUser,
    IAuthorizationService authorizationService) : EndpointWithoutRequest<GetMeResponse>
{
    public override void Configure()
    {
        Get("/api/me");
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

        if (!Guid.TryParse(currentUser.TenantId, out var companyId))
        {
            await SendResultAsync(TypedResults.Forbid());
            return;
        }

        var permissions = await authorizationService.GetEffectivePermissionsAsync(userId.Value, ct);

        await SendAsync(new GetMeResponse(
            userId.Value,
            companyId,
            currentUser.Email,
            permissions.ToList()), StatusCodes.Status200OK, ct);
    }
}

internal sealed record GetMeResponse(
    Guid UserId,
    Guid CompanyId,
    string? Email,
    List<Guid> PermissionIds);
