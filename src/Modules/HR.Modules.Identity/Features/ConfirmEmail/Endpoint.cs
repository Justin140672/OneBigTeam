using FastEndpoints;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace HR.Modules.Identity.Features.ConfirmEmail;

// Dev-only bypass for the real Supabase-Auth email-confirmation flow (out of scope for now — see
// ApplicationUser.IsEmailConfirmed remarks). In the real system an account can only be confirmed
// by clicking the link in a real confirmation email — there is deliberately no in-app "confirm
// now" action offered to real users (see EmailConfirmationRequired.razor). Since no confirmation
// email is actually sent yet, this endpoint exists purely so local testing/demos aren't
// permanently stuck behind the block; it 404s outside Development, mirroring the /api/dev/*
// minimal-API endpoints in HR.Api's Program.cs.
internal sealed class Endpoint(
    ICurrentUser currentUser,
    IdentityDbContext dbContext,
    IWebHostEnvironment environment,
    IClock clock) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/dev/confirm-email");
        Policies("role:employee");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!environment.IsDevelopment())
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        var userId = currentUser.UserId;
        if (userId is null)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.Id == userId.Value, ct);
        if (user is null)
        {
            await Send.ResultAsync(TypedResults.Forbid());
            return;
        }

        user.ConfirmEmail(clock.UtcNow);
        await dbContext.SaveChangesAsync(ct);

        await Send.NoContentAsync(ct);
    }
}
