using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity")));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
        services.AddScoped<HR.SharedKernel.IAuthorizationService, IdentityAuthorizationService>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, RoleAuthorizationHandler>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }

    public static IApplicationBuilder UseIdentityModule(this IApplicationBuilder app)
    {
        app.UseMiddleware<SupabaseCurrentUserResolutionMiddleware>();
        app.UseMiddleware<RequireTenantMiddleware>();
        app.UseMiddleware<TenantRouteAuthorizationMiddleware>();
        return app;
    }

    public static AuthorizationBuilder AddRolePolicies(this AuthorizationBuilder builder)
    {
        // Individual role policies
        builder.AddPolicy("role:employee",             RolePolicy(SystemRoles.Employee));
        builder.AddPolicy("role:manager",              RolePolicy(SystemRoles.Manager));
        builder.AddPolicy("role:recruiter",            RolePolicy(SystemRoles.Recruiter));
        builder.AddPolicy("role:hr-administrator",     RolePolicy(SystemRoles.HrAdministrator));
        builder.AddPolicy("role:company-administrator",RolePolicy(SystemRoles.CompanyAdministrator));

        // Employee domain policies — match spec section 12/13.
        // Company Administrator is scoped to company profile/settings only and must not
        // manage employee/HR data — see the mirror-image rule on company:manage below.
        builder.AddPolicy("employee:manage", RolePolicy(
            SystemRoles.HrAdministrator));

        // Company domain policies — match spec section 24.
        // Company profile/settings/branding are Company Administrator territory only —
        // HR Administrator is a distinct role scoped to employee/leave/sickness data and
        // must not be able to change company-level configuration.
        builder.AddPolicy("company:manage", RolePolicy(
            SystemRoles.CompanyAdministrator));

        // Leave domain composite policies — match spec section 14.
        // Company Administrator no longer participates in HR workflows (see employee:manage above).
        builder.AddPolicy("leave:request", RolePolicy(
            SystemRoles.Employee,
            SystemRoles.Manager,
            SystemRoles.HrAdministrator));

        builder.AddPolicy("leave:approve", RolePolicy(
            SystemRoles.Manager,
            SystemRoles.HrAdministrator));

        builder.AddPolicy("leave:manage", RolePolicy(
            SystemRoles.HrAdministrator));

        builder.AddPolicy("probation:manage", RolePolicy(
            SystemRoles.HrAdministrator));

        builder.AddPolicy("probation:review", RolePolicy(
            SystemRoles.Manager,
            SystemRoles.HrAdministrator));

        // Sickness domain — read access to a manager's assigned return-to-work review task
        builder.AddPolicy("sickness:review", RolePolicy(
            SystemRoles.Manager,
            SystemRoles.HrAdministrator));

        // Sickness domain — HR-only management of categories/records
        builder.AddPolicy("sickness:manage", RolePolicy(
            SystemRoles.HrAdministrator));

        // Sickness domain — team visibility for managers plus HR auditing
        builder.AddPolicy("sickness:view-team", RolePolicy(
            SystemRoles.Manager,
            SystemRoles.HrAdministrator));

        // Asset domain policies
        builder.AddPolicy("asset:view", RolePolicy(
            SystemRoles.Employee,
            SystemRoles.Manager,
            SystemRoles.HrAdministrator));

        // Recruitment domain policies
        // Recruiter-only: HR Administrator does NOT automatically get recruitment access — unlike
        // employee:manage-adjacent domains elsewhere in this file, recruitment is a distinct
        // function with its own role, and an HR Administrator needs the Recruiter role too (same
        // non-overlap principle as company:manage/shared-document management below).
        builder.AddPolicy("recruitment:manage", RolePolicy(
            SystemRoles.Recruiter));

        // Recruitment domain — vacancy-only reads (internal job board visibility).
        // Broad by design: seeing what roles are open is general visibility, not sensitive —
        // unlike recruitment:manage/candidate:view above, this one deliberately keeps
        // HrAdministrator (and every other role) able to read published vacancies.
        builder.AddPolicy("recruitment:view", RolePolicy(
            SystemRoles.Employee,
            SystemRoles.Manager,
            SystemRoles.Recruiter,
            SystemRoles.HrAdministrator));

        // Recruitment domain — candidate/application/interview/document reads. Recruiter-only,
        // same non-overlap reasoning as recruitment:manage above: candidate PII, resumes, and
        // interview notes must not be visible to plain Employees/Managers, nor automatically to
        // HR Administrators who lack the Recruiter role.
        builder.AddPolicy("candidate:view", RolePolicy(
            SystemRoles.Recruiter));

        // Shared company document domain policies (documents owned by the company as a whole,
        // e.g. policies/handbooks — distinct from an employee's own document records).
        //
        // Viewing published documents is broad by design, same reasoning as recruitment:view —
        // any real employee should be able to read company policies. Managing/publishing/
        // archiving/acknowledgement-status stay HR-only: Company Administrator does NOT
        // automatically get access here (same non-overlap rule as employee:manage/company:manage
        // above — a Company Administrator needs the HrAdministrator role too, not just
        // CompanyAdministrator, to manage these), and Manager does not automatically get manage
        // rights either, unlike leave:approve/probation:review/sickness:review.
        builder.AddPolicy("shared-document:view-published", RolePolicy(
            SystemRoles.Employee,
            SystemRoles.Manager,
            SystemRoles.Recruiter,
            SystemRoles.HrAdministrator));

        builder.AddPolicy("shared-document:manage", RolePolicy(
            SystemRoles.HrAdministrator));

        builder.AddPolicy("shared-document:publish", RolePolicy(
            SystemRoles.HrAdministrator));

        builder.AddPolicy("shared-document:archive", RolePolicy(
            SystemRoles.HrAdministrator));

        builder.AddPolicy("shared-document:view-acknowledgement-status", RolePolicy(
            SystemRoles.HrAdministrator));

        return builder;
    }

    private static Action<AuthorizationPolicyBuilder> RolePolicy(params Guid[] roleIds) =>
        policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new RoleRequirement(roleIds.ToHashSet()));

    public static async Task MigrateIdentityAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS identity");
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// Seeds development personas with identity records and appropriate roles so
    /// the DevAuthHandler has valid, permission-bearing identities for each persona.
    /// Safe to call on every startup: reconciles role assignments to match the list
    /// below, adding missing roles and removing ones no longer declared.
    /// </summary>
    public static async Task SeedDevUserAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;

        var personas = new[]
        {
            (Id: new Guid("30000000-0000-0000-0000-000000000001"), First: "Sarah",  Last: "Chen",    Email: "sarah.chen@acme.example",         Roles: new[] { SystemRoles.Employee, SystemRoles.CompanyAdministrator, SystemRoles.Manager }),
            (Id: new Guid("30000000-0000-0000-0000-000000000002"), First: "James",  Last: "Okafor",  Email: "james.okafor@acme.example",       Roles: new[] { SystemRoles.Employee, SystemRoles.Manager }),
            (Id: new Guid("30000000-0000-0000-0000-000000000004"), First: "Tom",    Last: "Williams", Email: "tom.williams@acme.example",       Roles: new[] { SystemRoles.Employee }),
            (Id: new Guid("30000000-0000-0000-0000-000000000010"), First: "Carlos", Last: "Rivera",   Email: "carlos.rivera@acme.example",      Roles: new[] { SystemRoles.Employee }),
            (Id: new Guid("30000000-0000-0000-0000-000000000005"), First: "Laura",  Last: "Bennett", Email: "laura.bennett@acme.example",       Roles: new[] { SystemRoles.Employee, SystemRoles.HrAdministrator }),
            (Id: new Guid("30000000-0000-0000-0000-000000000006"), First: "Marcus", Last: "Diallo",  Email: "marcus.diallo@acme.example",       Roles: new[] { SystemRoles.Employee, SystemRoles.Recruiter }),
            (Id: new Guid("30000000-0000-0000-0000-000000000008"), First: "David",  Last: "Park",    Email: "david.park@acme.example",          Roles: new[] { SystemRoles.Employee, SystemRoles.HrAdministrator, SystemRoles.Manager }),
            (Id: new Guid("30000000-0000-0000-0000-000000000013"), First: "Priya",  Last: "Shah",    Email: "priya.shah@acme.example",          Roles: new[] { SystemRoles.Employee, SystemRoles.CompanyAdministrator }),
            (Id: new Guid("30000000-0000-0000-0000-000000000011"), First: "Alice",  Last: "Morgan",  Email: "alice.morgan@betacorp.example",    Roles: new[] { SystemRoles.Employee, SystemRoles.Manager }),
            (Id: new Guid("30000000-0000-0000-0000-000000000012"), First: "Bob",    Last: "Taylor",  Email: "bob.taylor@betacorp.example",      Roles: new[] { SystemRoles.Employee }),
        };

        foreach (var persona in personas)
        {
            var exists = await db.Users.AnyAsync(u => u.Id == persona.Id);
            if (!exists)
            {
                db.Users.Add(ApplicationUser.Create(
                    persona.Id, persona.Email,
                    passwordHash: "dev-only-not-used",
                    firstName: persona.First, lastName: persona.Last, now));
            }

            foreach (var roleId in persona.Roles)
            {
                var roleExists = await db.UserRoles.AnyAsync(
                    ur => ur.UserId == persona.Id && ur.RoleId == roleId);
                if (!roleExists)
                    db.UserRoles.Add(UserRole.Create(persona.Id, roleId, now));
            }

            // Reconcile: drop any previously-seeded roles that are no longer declared above,
            // so re-running against an existing dev database converges on the current mapping.
            var stale = await db.UserRoles
                .Where(ur => ur.UserId == persona.Id && !persona.Roles.Contains(ur.RoleId))
                .ToListAsync();
            db.UserRoles.RemoveRange(stale);
        }

        await db.SaveChangesAsync();
    }
}
