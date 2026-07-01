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
        builder.AddPolicy("role:finance",              RolePolicy(SystemRoles.Finance));
        builder.AddPolicy("role:company-administrator",RolePolicy(SystemRoles.CompanyAdministrator));

        // Employee domain policies — match spec section 12/13
        builder.AddPolicy("employee:manage", RolePolicy(
            SystemRoles.HrAdministrator,
            SystemRoles.CompanyAdministrator));

        // Company domain policies — match spec section 24
        builder.AddPolicy("company:manage", RolePolicy(
            SystemRoles.HrAdministrator,
            SystemRoles.CompanyAdministrator));

        // Leave domain composite policies — match spec section 14
        builder.AddPolicy("leave:request", RolePolicy(
            SystemRoles.Employee,
            SystemRoles.Manager,
            SystemRoles.HrAdministrator,
            SystemRoles.CompanyAdministrator));

        builder.AddPolicy("leave:approve", RolePolicy(
            SystemRoles.Manager,
            SystemRoles.HrAdministrator,
            SystemRoles.CompanyAdministrator));

        builder.AddPolicy("leave:manage", RolePolicy(
            SystemRoles.HrAdministrator,
            SystemRoles.CompanyAdministrator));

        builder.AddPolicy("probation:manage", RolePolicy(
            SystemRoles.HrAdministrator,
            SystemRoles.CompanyAdministrator));

        // Asset domain policies
        builder.AddPolicy("asset:view", RolePolicy(
            SystemRoles.Employee,
            SystemRoles.Manager,
            SystemRoles.HrAdministrator,
            SystemRoles.CompanyAdministrator));

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
    /// Safe to call on every startup (idempotent).
    /// </summary>
    public static async Task SeedDevUserAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;

        var personas = new[]
        {
            (Id: new Guid("30000000-0000-0000-0000-000000000001"), First: "Sarah",  Last: "Chen",    Email: "sarah.chen@acme.example",         Roles: new[] { SystemRoles.HrAdministrator }),
            (Id: new Guid("30000000-0000-0000-0000-000000000002"), First: "James",  Last: "Okafor",  Email: "james.okafor@acme.example",       Roles: new[] { SystemRoles.Employee, SystemRoles.Manager }),
            (Id: new Guid("30000000-0000-0000-0000-000000000004"), First: "Tom",    Last: "Williams", Email: "tom.williams@acme.example",       Roles: new[] { SystemRoles.Employee }),
            (Id: new Guid("30000000-0000-0000-0000-000000000010"), First: "Carlos", Last: "Rivera",   Email: "carlos.rivera@acme.example",      Roles: new[] { SystemRoles.Employee }),
            (Id: new Guid("30000000-0000-0000-0000-000000000005"), First: "Laura",  Last: "Bennett", Email: "laura.bennett@acme.example",       Roles: new[] { SystemRoles.HrAdministrator }),
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
        }

        await db.SaveChangesAsync();
    }
}
