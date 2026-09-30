using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Services;

internal static class StagingUsersSeeder
{
    internal static readonly IReadOnlyDictionary<string, Guid[]> EmployeeLoginRoles = new Dictionary<string, Guid[]>
    {
        ["Sarah Chen"] = [SystemRoles.Employee, SystemRoles.CompanyAdministrator, SystemRoles.Manager],
        ["Priya Shah"] = [SystemRoles.Employee, SystemRoles.CompanyAdministrator],
        ["Laura Bennett"] = [SystemRoles.Employee, SystemRoles.HrAdministrator],
        ["Marcus Diallo"] = [SystemRoles.Employee, SystemRoles.HrAdministrator, SystemRoles.Recruiter],
        ["Nina Patel"] = [SystemRoles.Employee, SystemRoles.Manager, SystemRoles.Recruiter],
        ["Olivia Reyes"] = [SystemRoles.Employee, SystemRoles.Recruiter],
        ["James Okafor"] = [SystemRoles.Employee, SystemRoles.Manager],
        ["Tom Williams"] = [SystemRoles.Employee],
        ["Emma Jones"] = [SystemRoles.Employee],
    };

    private sealed record Login(Guid Id, string Email, string FirstName, string LastName, Guid[] Roles);

    public static async Task SeedAsync(
        IServiceProvider services,
        StagingSeedOptions options,
        IEnumerable<(Guid Id, Guid CompanyId, string Email, string FirstName, string LastName)> employees)
    {
        var gateway = services.GetRequiredService<ISupabaseAuthGateway>();
        var db = services.GetRequiredService<IdentityDbContext>();
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("HR.Modules.Identity.StagingUsersSeeder");
        var password = options.Password!;
        var companyId = StagingSeedOptions.CompanyId;

        var logins = new List<Login>();

        var employeesByName = employees.ToDictionary(e => $"{e.FirstName} {e.LastName}");
        foreach (var (name, roles) in EmployeeLoginRoles)
        {
            if (!employeesByName.TryGetValue(name, out var employee))
            {
                throw new InvalidOperationException(
                    $"Staging seed login '{name}' does not match any seeded staging employee.");
            }

            logins.Add(new Login(employee.Id, employee.Email, employee.FirstName, employee.LastName, roles));
        }

        foreach (var login in logins)
        {
            try
            {
                await EnsureLoginAsync(gateway, db, companyId, password, login);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex,
                    "Failed to seed staging login for user {UserId} — skipping, other logins unaffected; it will be retried on next startup.",
                    login.Id);

                foreach (var entry in db.ChangeTracker.Entries().ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }
        }
    }

    private static async Task EnsureLoginAsync(
        ISupabaseAuthGateway gateway, IdentityDbContext db, Guid companyId, string password, Login login)
    {
        var now = DateTimeOffset.UtcNow;

        var supabaseUserId = await gateway.GetUserIdByEmailAsync(login.Email, CancellationToken.None);
        if (supabaseUserId is null)
        {
            try
            {
                supabaseUserId = await gateway.CreateConfirmedUserAsync(login.Email, password, CancellationToken.None);
            }
            catch (EmailAlreadyRegisteredException)
            {
                supabaseUserId = await gateway.GetUserIdByEmailAsync(login.Email, CancellationToken.None)
                    ?? throw new InvalidOperationException(
                        $"Supabase reported '{login.Email}' as registered but no user could be resolved.");
            }
        }

        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == login.Id);
        if (profile is null)
        {
            db.UserProfiles.Add(UserProfile.Create(
                login.Id, supabaseUserId.Value, companyId, login.Email, login.FirstName, login.LastName, now));
        }
        else if (profile.SupabaseAuthUserId != supabaseUserId.Value)
        {
            profile.UpdateSupabaseAuthUserId(supabaseUserId.Value, now);
        }

        var existingRoleIds = await db.UserRoles
            .Where(ur => ur.UserId == login.Id)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        foreach (var roleId in login.Roles.Except(existingRoleIds))
        {
            db.UserRoles.Add(UserRole.Create(login.Id, roleId, now));
        }

        await db.SaveChangesAsync();
    }
}
