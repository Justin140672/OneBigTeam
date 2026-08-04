using FluentValidation;
using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.CancelInvite;
using HR.Modules.Identity.Features.DisableUser;
using HR.Modules.Identity.Features.EnableUser;
using HR.Modules.Identity.Features.GetUserAuditHistory;
using HR.Modules.Identity.Features.GetUserDetails;
using HR.Modules.Identity.Features.InviteEmployeeUser;
using HR.Modules.Identity.Features.ListUsers;
using HR.Modules.Identity.Features.ResendInvite;
using HR.Modules.Identity.Features.ResendVerification;
using HR.Modules.Identity.Features.SignUp;
using HR.Modules.Identity.Features.UpdateUserRoles;
using HR.Modules.Identity.Features.VerifyEmail;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Services.OnboardingTasks;
using HR.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Modules.Identity;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__ef_migrations_history", "identity")));
        services.AddHttpContextAccessor();

        services.Configure<SupabaseAuthOptions>(configuration.GetSection("SupabaseAuth"));
        services.AddHttpClient();
        services.AddScoped<ISupabaseAuthGateway, SupabaseAuthGateway>();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
        services.AddScoped<HR.SharedKernel.IAuthorizationService, IdentityAuthorizationService>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, RoleAuthorizationHandler>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<IEmployeeUserAccountStatusReader, EmployeeUserAccountStatusReader>();
        services.AddScoped<IUserEmailReader, UserEmailReader>();

        services.AddScoped<ListUsersHandler>();
        services.AddScoped<IValidator<ListUsersRequest>, ListUsersValidator>();
        services.AddScoped<GetUserDetailsHandler>();
        services.AddScoped<IValidator<GetUserDetailsRequest>, GetUserDetailsValidator>();
        services.AddScoped<GetUserAuditHistoryHandler>();
        services.AddScoped<IValidator<GetUserAuditHistoryRequest>, GetUserAuditHistoryValidator>();
        services.AddScoped<InviteEmployeeUserHandler>();
        services.AddScoped<IValidator<InviteEmployeeUserRequest>, InviteEmployeeUserValidator>();
        services.AddScoped<UpdateUserRolesHandler>();
        services.AddScoped<IValidator<UpdateUserRolesRequest>, UpdateUserRolesValidator>();
        services.AddScoped<ResendInviteHandler>();
        services.AddScoped<IValidator<ResendInviteRequest>, ResendInviteValidator>();
        services.AddScoped<CancelInviteHandler>();
        services.AddScoped<IValidator<CancelInviteRequest>, CancelInviteValidator>();
        services.AddScoped<DisableUserHandler>();
        services.AddScoped<IValidator<DisableUserRequest>, DisableUserValidator>();
        services.AddScoped<EnableUserHandler>();
        services.AddScoped<IValidator<EnableUserRequest>, EnableUserValidator>();
        services.AddScoped<SignUpHandler>();
        services.AddScoped<IValidator<SignUpRequest>, SignUpValidator>();
        services.AddScoped<ResendVerificationHandler>();
        services.AddScoped<IValidator<ResendVerificationRequest>, ResendVerificationValidator>();
        services.AddScoped<VerifyEmailHandler>();

        services.AddScoped<
            IIntegrationEventHandler<OffboardingPlanCompletedIntegrationEvent>,
            Features.OnOffboardingPlanCompleted.Handler>();

        services.AddScoped<IWorkloadActionProvider, EmployeeAccountsAwaitingInvitationWorkloadActionProvider>();
        services.AddScoped<IWorkloadActionProvider, EmployeeAccountsAwaitingDisablementWorkloadActionProvider>();

        // Getting Started checklist task definition (HR.Modules.CompanyOnboarding epic, Phase A).
        services.AddScoped<IOnboardingTaskDefinition, InviteAdditionalUsersTask>();

        return services;
    }

    /// <summary>
    /// Called from HR.Api's dev persona-switch endpoint (the only real "sign-in" path that exists
    /// today — see the class remarks on Authentication.DevAuthHandler). Rejects switching to a
    /// disabled user's persona and records LastLoginAt on success. This will need to be revisited
    /// once real Supabase-backed authentication replaces the dev persona switcher.
    /// Returns false if the persona's linked user account is disabled (sign-in must be rejected);
    /// true otherwise (allowed — including when no ApplicationUser row exists at all).
    /// </summary>
    public static async Task<bool> TryDevSignInAsync(this IServiceProvider services, Guid userId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return true; // no ApplicationUser row (e.g. persona seeded only in dev store) — allow, nothing to gate.

        if (!user.IsActive)
            return false;

        user.RecordLogin(clock.UtcNow);
        await db.SaveChangesAsync();
        return true;
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

        // Support & Feedback domain policy — status management and the reporting dashboard are
        // internal-staff territory. No dedicated "platform staff" role exists yet in SystemRoles,
        // so this is scoped to HR/Company Administrator (same OR-of-roles shape as users:view
        // above) as the closest existing approximation. Revisit if a genuine internal-staff role
        // is introduced later.
        builder.AddPolicy("support:manage", RolePolicy(
            SystemRoles.HrAdministrator,
            SystemRoles.CompanyAdministrator));

        // HR Settings domain policy — the HR-policy fields split out of Company Settings
        // (working pattern, leave defaults, probation, sickness, employee profile display-salary,
        // acknowledgements, leaving/offboarding, employee numbering) are HR Administrator
        // territory only. Company Administrator must not manage HR policy, matching the
        // mirror-image rule on company:manage above.
        builder.AddPolicy("hr-settings:manage", RolePolicy(
            SystemRoles.HrAdministrator));

        // User Administration domain policies (ticket #92) — viewing/inviting/managing roles and
        // disabling/enabling accounts, plus resending/cancelling invitations, are HR/Company Admin
        // territory, matching employee:manage/company:manage's precedent of restricting
        // security-sensitive actions to administrative roles.
        builder.AddPolicy("users:view", RolePolicy(
            SystemRoles.HrAdministrator,
            SystemRoles.CompanyAdministrator));

        builder.AddPolicy("users:manage", RolePolicy(
            SystemRoles.HrAdministrator,
            SystemRoles.CompanyAdministrator));

        // Getting Started checklist domain policies (CompanyOnboarding epic, Phase A) — same
        // HR/Company Admin OR-of-roles shape as users:view/users:manage above.
        builder.AddPolicy("onboarding:view", RolePolicy(
            SystemRoles.HrAdministrator,
            SystemRoles.CompanyAdministrator));

        builder.AddPolicy("onboarding:manage", RolePolicy(
            SystemRoles.HrAdministrator,
            SystemRoles.CompanyAdministrator));

        // Subscription domain policy (Phase C — Stripe checkout) — same HR/Company Admin
        // OR-of-roles shape as onboarding:manage/users:manage above.
        builder.AddPolicy("subscription:manage", RolePolicy(
            SystemRoles.HrAdministrator,
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

        // Reporting domain policies (Reporting Dashboard epic, phase 1).
        // "reporting:view" is the baseline gate for the reporting area — Manager, Recruiter,
        // and HrAdministrator only; plain Employees have no reporting access. Category-scoped
        // policies below are deliberately non-overlapping (same precedent as
        // recruitment:manage/candidate:view above): a Recruiter without HrAdministrator sees
        // only the recruitment category, and an HrAdministrator without Recruiter sees only
        // the HR category. A user needs both roles to see both categories.
        builder.AddPolicy("reporting:view", RolePolicy(
            SystemRoles.Manager,
            SystemRoles.Recruiter,
            SystemRoles.HrAdministrator));

        builder.AddPolicy("reporting:view-recruitment", RolePolicy(
            SystemRoles.Recruiter));

        builder.AddPolicy("reporting:view-hr", RolePolicy(
            SystemRoles.HrAdministrator));

        // Reporting Dashboard epic, phase 2 (OBT-704..707). Employee Starter Report is HR
        // territory but is also explicitly relevant to Recruiters tracking their own placements
        // (ticket OBT-704) — combined OR-of-roles policy, same RolePolicy mechanism used above,
        // rather than requiring both reporting:view-hr AND reporting:view-recruitment (which would
        // be an AND and wrongly exclude a Recruiter-only user).
        builder.AddPolicy("reporting:view-employee-starter", RolePolicy(
            SystemRoles.HrAdministrator,
            SystemRoles.Recruiter));

        // Leave Summary Report (OBT-706) is HR-visible company-wide, but the ticket doesn't
        // restrict it to HR only — a Manager should be able to see their own team's leave summary.
        // This policy grants baseline endpoint access to both roles; the handler is responsible for
        // scoping a non-HR caller down to their direct reports via IDirectReportsReader so a
        // Manager never sees company-wide data through this endpoint (row-level scoping, not just a
        // relaxed policy — see GetLeaveSummaryReport/Handler.cs).
        builder.AddPolicy("reporting:view-leave-summary", RolePolicy(
            SystemRoles.HrAdministrator,
            SystemRoles.Manager));

        // Probation Report (OBT-711) — modelled exactly on reporting:view-leave-summary above: a
        // Manager gets baseline endpoint access, but the handler hard-restricts a non-HR caller to
        // their own direct reports via IDirectReportsReader (row-level scoping, never company-wide
        // data — see GetProbationReport/Handler.cs).
        builder.AddPolicy("reporting:view-probation", RolePolicy(
            SystemRoles.HrAdministrator,
            SystemRoles.Manager));

        // Onboarding Progress Report (OBT-712) — same OR-of-roles shape as reporting:view-probation:
        // a Manager gets baseline endpoint access, handler restricts non-HR callers to their own
        // direct reports.
        builder.AddPolicy("reporting:view-onboarding", RolePolicy(
            SystemRoles.HrAdministrator,
            SystemRoles.Manager));

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
