using System.Security.Cryptography;
using System.Text;
using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Identity.Features.SignUp;

// Self-service signup: creates a brand-new Company (via the sanctioned ICompanyProvisioner
// cross-module contract, so Identity never references HR.Modules.Companies directly) plus a local
// admin ApplicationUser, mirroring AcceptInvite's local SHA256 password hashing rather than
// introducing real Supabase Auth (out of scope for this epic — see plan notes).
//
// Note: company provisioning (Companies schema) and admin user creation (Identity schema) are
// committed as two separate SaveChanges — there is no cross-module distributed transaction
// mechanism in this architecture (each module owns its own DbContext/connection). If the second
// step fails, an orphaned company with no admin user could result; acceptable for this phase given
// low signup volume, but worth revisiting if this becomes a real reliability concern.
internal sealed class SignUpHandler(
    IdentityDbContext dbContext,
    ICompanyProvisioner companyProvisioner,
    IClock clock)
{
    public async Task<Result<SignUpResponse>> HandleAsync(SignUpRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.AdminEmail.Trim().ToUpperInvariant();

        var emailInUse = await dbContext.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (emailInUse)
        {
            return Result.Failure<SignUpResponse>(Error.Conflict("An account with this email already exists."));
        }

        var now = clock.UtcNow;

        var companyId = await companyProvisioner.ProvisionCompanyAsync(request.CompanyName.Trim(), cancellationToken);

        var user = ApplicationUser.Create(
            Guid.NewGuid(),
            request.AdminEmail.Trim(),
            passwordHash: HashPassword(request.Password),
            firstName: request.AdminFirstName.Trim(),
            lastName: request.AdminLastName.Trim(),
            now,
            isEmailConfirmed: false);
        dbContext.Users.Add(user);

        // Every seeded persona carries SystemRoles.Employee alongside their specific role (see
        // IdentityModule's dev seed data) — it's the floor role required by "role:employee",
        // which gates core session endpoints (GetMe, GetCompany, etc.) that AppSession depends on
        // for every page. Without it, a self-service admin would 403 on first load.
        dbContext.UserRoles.Add(UserRole.Create(user.Id, SystemRoles.Employee, now));
        dbContext.UserRoles.Add(UserRole.Create(user.Id, SystemRoles.CompanyAdministrator, now));

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new SignUpResponse(user.Id, companyId, user.Email, user.FirstName, user.LastName);
        return Result.Success(response);
    }

    private static string HashPassword(string password)
    {
        // Mirrors AcceptInvite/Endpoint.cs's HashPassword exactly — same local-auth convention.
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
    }
}
