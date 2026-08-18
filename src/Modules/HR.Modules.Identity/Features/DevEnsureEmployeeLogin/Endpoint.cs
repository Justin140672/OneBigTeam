using FastEndpoints;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace HR.Modules.Identity.Features.DevEnsureEmployeeLogin;

// Dev-only endpoint that lets E2E/local-dev tooling give an arbitrary, freshly-created employee a
// real, working Supabase login — the same building block IdentityModule.EnsureDevSupabaseUserAsync
// already provides for the self-service SignUp flow (see POST /api/dev/persona/register), just not
// previously reachable for an ordinary employee created through the normal "New Employee" UI.
//
// Why this is needed: a brand-new Employee row has no linked UserProfile by construction (see
// EmployeeUserAccountColumnTests' remarks — "employee creation does not provision one"). The only
// production path that links one is the invite-accept-password-setup flow, which has no E2E UI
// coverage here. Some E2E scenarios (e.g. asset acknowledge/return task completion) must log in
// AS the employee to drive a self-service action, and reusing a shared seeded persona for that
// causes cross-test pollution under parallel execution because completing those tasks mutates
// shared seed data irreversibly. This endpoint lets such tests provision an isolated employee's
// login instead, exactly mirroring how the four canonical dev personas already get theirs (see
// IdentityModule.SeedDevSupabaseUsersAsync) — EmployeeId is reused as the UserProfile's Id, the
// same convention DevPersonaStore's seed list and /api/dev/persona/register both rely on.
//
// Idempotent (delegates entirely to EnsureDevSupabaseUserAsync, whose own remarks establish this),
// and self-heals a stale SupabaseAuthUserId the same way the signup path does, so calling this more
// than once for the same employee (e.g. a retried test) is safe. 404s outside Development, mirroring
// every other /api/dev/* endpoint (see DevActivateCompany.Endpoint).
internal sealed class Endpoint(
    IServiceProvider serviceProvider,
    IWebHostEnvironment environment) : Endpoint<DevEnsureEmployeeLoginRequest>
{
    public override void Configure()
    {
        Post("/api/dev/ensure-employee-login");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DevEnsureEmployeeLoginRequest request, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            await Send.ResultAsync(TypedResults.NotFound());
            return;
        }

        await serviceProvider.EnsureDevSupabaseUserAsync(
            request.EmployeeId, request.CompanyId, request.Email,
            request.FirstName, request.LastName, cancellationToken);

        await Send.NoContentAsync(cancellationToken);
    }
}
