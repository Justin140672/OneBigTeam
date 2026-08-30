using FluentValidation;
using Hangfire;
using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Authorization;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Features.AddEmployeeRoleOverride;
using HR.Modules.Identity.Features.AssignPlatformAdministratorRole;
using HR.Modules.Identity.Features.CancelInvite;
using HR.Modules.Identity.Features.CreatePlatformAdministrator;
using HR.Modules.Identity.Features.DisablePlatformAdministrator;
using HR.Modules.Identity.Features.DisableUser;
using HR.Modules.Identity.Features.EnablePlatformAdministrator;
using HR.Modules.Identity.Features.EnableUser;
using HR.Modules.Identity.Features.ExportAccessReview;
using HR.Modules.Identity.Features.GetAccessReview;
using HR.Modules.Identity.Features.GetEffectiveAccess;
using HR.Modules.Identity.Features.GetPermissionHistory;
using HR.Modules.Identity.Features.GetUserAuditHistory;
using HR.Modules.Identity.Features.GetUserDetails;
using HR.Modules.Identity.Features.InviteEmployeeUser;
using HR.Modules.Identity.Features.ListEmployeeRoleOverrides;
using HR.Modules.Identity.Features.ListPlatformAdministrators;
using HR.Modules.Identity.Features.ListPositionRoleDefaults;
using HR.Modules.Identity.Features.ListUsers;
using HR.Modules.Identity.Features.SearchUserAccess;
using HR.Modules.Identity.Features.Login;
using HR.Modules.Identity.Features.RemoveEmployeeRoleOverride;
using HR.Modules.Identity.Features.RequestPasswordReset;
using HR.Modules.Identity.Features.ResendInvite;
using HR.Modules.Identity.Features.ResendVerification;
using HR.Modules.Identity.Features.ResetPassword;
using HR.Modules.Identity.Features.ResetPlatformAdministratorMfa;
using HR.Modules.Identity.Features.ResetPlatformAdministratorPassword;
using HR.Modules.Identity.Features.SetPositionRoleDefaults;
using HR.Modules.Identity.Features.SignUp;
using HR.Modules.Identity.Features.UpdateUserRoles;
using HR.Modules.Identity.Features.VerifyEmail;
using HR.Modules.Identity.Jobs;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Services.OnboardingTasks;
using HR.Modules.Employees.Contracts;
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

        // The real gateway makes genuine HTTP calls to Supabase's Auth Admin API — the E2E suite's
        // signup/resend journeys create a real pending user per run, which hits Supabase's rate
        // limits under repeated local/CI runs. Swap in a no-op fake for those runs (AppFixture sets
        // E2E_TESTING=true, same flag HR.AppHost already reads).
        var isE2ETesting = string.Equals(
            Environment.GetEnvironmentVariable("E2E_TESTING"), "true", StringComparison.OrdinalIgnoreCase);
        if (isE2ETesting)
        {
            services.AddScoped<ISupabaseAuthGateway, FakeSupabaseAuthGateway>();
        }
        else
        {
            services.AddScoped<ISupabaseAuthGateway, SupabaseAuthGateway>();
        }

        // System Health Dashboard (Platform Monitoring epic) — "auth" named health check, live
        // reachability probe against Supabase Auth's public settings endpoint (see
        // SupabaseAuthHealthCheck remarks).
        services.AddHealthChecks()
            .AddCheck<SupabaseAuthHealthCheck>("auth");

        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
        // IAM-01: reusable target-user company-membership guard used by every user-administration
        // handler that resolves a target user/employee by id from the route.
        services.AddScoped<HR.Modules.Identity.Authorization.ITargetUserCompanyGuard,
            HR.Modules.Identity.Authorization.TargetUserCompanyGuard>();
        services.AddScoped<HR.SharedKernel.IAuthorizationService, IdentityAuthorizationService>();
        services.AddScoped<HR.Modules.Identity.Authorization.LastActiveAdministratorGuard>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, RoleAuthorizationHandler>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PlatformAdminAuthorizationHandler>();
        // IAM-06: backs every named capability policy resolved through PolicyCatalog/PermissionPolicy.
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionAuthorizationHandler>();
        // IAM-08: process-wide denial-audit volume control — must be a singleton so the throttle
        // window is shared across requests/scopes, not reset per request.
        services.AddSingleton<PermissionDenialAuditThrottle>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<IEmployeeUserAccountStatusReader, EmployeeUserAccountStatusReader>();
        services.AddScoped<IHrAdministratorDirectory, HrAdministratorDirectory>();
        services.AddScoped<IUserEmailReader, UserEmailReader>();
        services.AddScoped<ICompanyUserEmailSearchReader, CompanyUserEmailSearchReader>();
        services.AddScoped<ICompanyUserCountReader, CompanyUserCountReader>();

        // Platform Audit Log (Audit epic) — resolves ActorUserId -> administrator email for the
        // Admin Portal's audit log grid/filter. Consumed by HR.Modules.Companies via this
        // Infrastructure.Abstractions interface, same pattern as ICompanyUserEmailSearchReader above.
        services.AddScoped<IUserEmailDirectoryReader, UserEmailDirectoryReader>();

        // Admin Portal Application Metrics dashboard (Platform Monitoring epic) — platform-wide,
        // not scoped to a single customer. Consumed by HR.Modules.Companies via this
        // Infrastructure.Abstractions interface.
        services.AddScoped<IPlatformUserActivityReader, PlatformUserActivityReader>();

        services.AddScoped<ListUsersHandler>();
        services.AddScoped<IValidator<ListUsersRequest>, ListUsersValidator>();
        services.AddScoped<GetUserDetailsHandler>();
        services.AddScoped<IValidator<GetUserDetailsRequest>, GetUserDetailsValidator>();
        services.AddScoped<GetUserAuditHistoryHandler>();
        services.AddScoped<IValidator<GetUserAuditHistoryRequest>, GetUserAuditHistoryValidator>();
        services.AddScoped<GetEffectiveAccessHandler>();
        services.AddScoped<IValidator<GetEffectiveAccessRequest>, GetEffectiveAccessValidator>();

        // IAM-08: permission history, search and access-review reporting.
        services.AddScoped<SearchUserAccessHandler>();
        services.AddScoped<IValidator<SearchUserAccessRequest>, SearchUserAccessValidator>();
        services.AddScoped<GetPermissionHistoryHandler>();
        services.AddScoped<IValidator<GetPermissionHistoryRequest>, GetPermissionHistoryValidator>();
        services.AddScoped<GetAccessReviewHandler>();
        services.AddScoped<IValidator<GetAccessReviewRequest>, GetAccessReviewValidator>();
        services.AddScoped<ExportAccessReviewHandler>();
        services.AddScoped<IValidator<ExportAccessReviewRequest>, ExportAccessReviewValidator>();
        services.AddScoped<InviteEmployeeUserHandler>();
        services.AddScoped<IValidator<InviteEmployeeUserRequest>, InviteEmployeeUserValidator>();
        services.AddScoped<HR.Modules.Identity.Features.ListInvitableEmployees.ListInvitableEmployeesHandler>();
        services.AddScoped<IValidator<HR.Modules.Identity.Features.ListInvitableEmployees.ListInvitableEmployeesRequest>,
            HR.Modules.Identity.Features.ListInvitableEmployees.ListInvitableEmployeesValidator>();
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

        services.AddScoped<LoginHandler>();
        services.AddScoped<IValidator<LoginRequest>, LoginValidator>();

        services.AddScoped<RequestPasswordResetHandler>();
        services.AddScoped<IValidator<RequestPasswordResetRequest>, RequestPasswordResetValidator>();

        services.AddScoped<ResetPasswordHandler>();
        services.AddScoped<IValidator<ResetPasswordRequest>, ResetPasswordValidator>();
        services.AddScoped<VerifyEmailHandler>();

        // Admin User Management (Admin Portal "administrator management" screen).
        services.AddScoped<CreatePlatformAdministratorHandler>();
        services.AddScoped<IValidator<CreatePlatformAdministratorRequest>, CreatePlatformAdministratorValidator>();
        services.AddScoped<DisablePlatformAdministratorHandler>();
        services.AddScoped<IValidator<DisablePlatformAdministratorRequest>, DisablePlatformAdministratorValidator>();
        services.AddScoped<EnablePlatformAdministratorHandler>();
        services.AddScoped<IValidator<EnablePlatformAdministratorRequest>, EnablePlatformAdministratorValidator>();
        services.AddScoped<AssignPlatformAdministratorRoleHandler>();
        services.AddScoped<IValidator<AssignPlatformAdministratorRoleRequest>, AssignPlatformAdministratorRoleValidator>();
        services.AddScoped<ListPlatformAdministratorsHandler>();
        services.AddScoped<IValidator<ListPlatformAdministratorsRequest>, ListPlatformAdministratorsValidator>();
        services.AddScoped<ResetPlatformAdministratorPasswordHandler>();
        services.AddScoped<IValidator<ResetPlatformAdministratorPasswordRequest>, ResetPlatformAdministratorPasswordValidator>();
        services.AddScoped<ResetPlatformAdministratorMfaHandler>();
        services.AddScoped<IValidator<ResetPlatformAdministratorMfaRequest>, ResetPlatformAdministratorMfaValidator>();

        services.AddScoped<
            IIntegrationEventHandler<OffboardingPlanCompletedIntegrationEvent>,
            Features.OnOffboardingPlanCompleted.Handler>();

        // IAM-03: position-based default role administration.
        services.AddScoped<HR.Modules.Identity.Services.PositionSync>();
        services.AddScoped<ListPositionRoleDefaultsHandler>();
        services.AddScoped<IValidator<ListPositionRoleDefaultsRequest>, ListPositionRoleDefaultsValidator>();
        services.AddScoped<SetPositionRoleDefaultsHandler>();
        services.AddScoped<IValidator<SetPositionRoleDefaultsRequest>, SetPositionRoleDefaultsValidator>();
        services.AddScoped<
            IIntegrationEventHandler<EmployeeCreatedIntegrationEvent>,
            Features.OnEmployeeCreated.Handler>();
        services.AddScoped<
            IIntegrationEventHandler<EmployeePositionChangedIntegrationEvent>,
            Features.OnEmployeePositionChanged.Handler>();
        services.AddScoped<
            IIntegrationEventHandler<PositionProfileUpsertedIntegrationEvent>,
            Features.OnPositionProfileUpserted.Handler>();

        // IAM-04: employee-level role-override administration.
        services.AddScoped<ListEmployeeRoleOverridesHandler>();
        services.AddScoped<IValidator<ListEmployeeRoleOverridesRequest>, ListEmployeeRoleOverridesValidator>();
        services.AddScoped<AddEmployeeRoleOverrideHandler>();
        services.AddScoped<IValidator<AddEmployeeRoleOverrideRequest>, AddEmployeeRoleOverrideValidator>();
        services.AddScoped<RemoveEmployeeRoleOverrideHandler>();
        services.AddScoped<IValidator<RemoveEmployeeRoleOverrideRequest>, RemoveEmployeeRoleOverrideValidator>();
        services.AddScoped<ExpireEmployeeRoleOverridesJob>();

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
        // Platform-admin policy — used exclusively by the new cross-tenant Admin Portal (Customer
        // Dashboard epic). Deliberately NOT built on RolePolicy/RoleRequirement: those require a
        // company-scoped RoleAssignment, but a platform administrator manages the whole platform
        // and may have no employee/company relationship at all. SEC-002 fix: this is now backed by
        // PlatformAdminAuthorizationHandler, which requires an enabled row in
        // identity.platform_administrators for the caller (matched by SupabaseAuthUserId, falling
        // back to email). Previously this only asserted "authenticated Supabase user", which let
        // any authenticated user of any tenant pass — a privilege-escalation hole surfaced by two
        // Companies-module handlers (GetPlatformSettings/UpdatePlatformSettings) that had no
        // additional handler-level check, unlike the ~23 other handlers that separately check the
        // PlatformAdmin:AllowedEmails config allow-list (left in place for now as defense-in-depth
        // — see PlatformAdminAuthorizationHandler remarks).
        builder.AddPolicy("platform:admin", policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new PlatformAdminRequirement()));

        // Individual role policies
        builder.AddPolicy("role:employee",             RolePolicy(SystemRoles.Employee));
        builder.AddPolicy("role:manager",              RolePolicy(SystemRoles.Manager));
        builder.AddPolicy("role:recruiter",            RolePolicy(SystemRoles.Recruiter));
        builder.AddPolicy("role:hr-administrator",     RolePolicy(SystemRoles.HrAdministrator));
        builder.AddPolicy("role:company-administrator",RolePolicy(SystemRoles.CompanyAdministrator));

        // IAM-06: every remaining named capability policy resolves through the authoritative
        // permission catalogue (PolicyCatalog -> SystemPermissions -> RolePermission grants) rather
        // than a role list duplicated inline per policy. This block is the single place these
        // policies are registered; the role -> permission grants that determine which of
        // Employee/Manager/Recruiter/HrAdministrator/CompanyAdministrator actually satisfy each one
        // live in Persistence/Configurations/RolePermissionConfiguration.cs (and are exercised by
        // HR.Modules.Identity.Tests' policy-matrix test), so a role-mapping change can never leave
        // this registration and the underlying data inconsistent with each other. See
        // PolicyCatalog.cs for the current product-decision reasoning behind each mapping (it
        // reproduces the same OR-of-roles authorization behaviour these policies already had).
        foreach (var (policyName, permissionId) in PolicyCatalog.PermissionPolicies)
        {
            builder.AddPolicy(policyName, PermissionPolicy(permissionId));
        }

        return builder;
    }

    private static Action<AuthorizationPolicyBuilder> RolePolicy(params Guid[] roleIds) =>
        policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new RoleRequirement(roleIds.ToHashSet()));

    private static Action<AuthorizationPolicyBuilder> PermissionPolicy(Guid permissionId) =>
        policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permissionId));

    public static async Task MigrateIdentityAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS identity");
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// IAM-04: daily sweep that clears out expired employee-level role overrides and audits the
    /// expiry (access is already enforced correctly before this runs — see
    /// IdentityAuthorizationService — this job only produces the audit trail and tidies the table).
    /// </summary>
    public static WebApplication UseIdentityRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<ExpireEmployeeRoleOverridesJob>(
            "expire-employee-role-overrides",
            job => job.ExecuteAsync(),
            Cron.Daily(2));
        return app;
    }

    /// <summary>
    /// IAM-03 backfill/reconciliation: ensures every current employee's position assignment
    /// (as of now, per HR.Modules.Employees) has a matching identity.user_positions row, for
    /// employees created before position-based role bridging existed (see
    /// Features/OnEmployeeCreated and Features/OnEmployeePositionChanged, which only fire on new
    /// integration-event traffic going forward). Called on every startup, in every environment,
    /// right after MigrateIdentityAsync — idempotent and additive only: it never expires or
    /// removes an existing UserPosition row, so it cannot clobber a manually-corrected assignment.
    /// Scoped per company (via UserProfile.CompanyId) so it never processes cross-tenant data.
    /// </summary>
    public static async Task ReconcilePositionRoleAssignmentsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var employeeAudienceReader = scope.ServiceProvider.GetRequiredService<IEmployeeAudienceReader>();
        var positionSync = scope.ServiceProvider.GetRequiredService<HR.Modules.Identity.Services.PositionSync>();
        var now = DateTimeOffset.UtcNow;

        var companyIds = await db.UserProfiles.Select(p => p.CompanyId).Distinct().ToListAsync();

        foreach (var companyId in companyIds)
        {
            var employeeIds = await employeeAudienceReader.GetAllEmployeeIdsAsync(companyId, CancellationToken.None);
            if (employeeIds.Count == 0)
                continue;

            var profiles = await employeeAudienceReader.GetEmployeeAudienceProfilesAsync(
                companyId, employeeIds, CancellationToken.None);

            foreach (var (employeeId, profile) in profiles)
            {
                if (profile.PositionProfileId is null)
                    continue;

                var positionId = profile.PositionProfileId.Value;

                var hasActiveAssignment = await db.UserPositions.AnyAsync(up =>
                    up.UserId == employeeId && up.PositionId == positionId &&
                    (up.ExpiresAt == null || up.ExpiresAt > now));
                if (hasActiveAssignment)
                    continue;

                await positionSync.EnsureExistsAsync(companyId, positionId, now, CancellationToken.None);
                db.UserPositions.Add(UserPosition.Create(employeeId, positionId, now));
            }
        }

        await db.SaveChangesAsync();
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
            (Id: new Guid("30000000-0000-0000-0000-000000000015"), First: "Grace",  Last: "Kim",     Email: "grace.kim@betacorp.example",       Roles: new[] { SystemRoles.Employee, SystemRoles.HrAdministrator }),
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

    /// <summary>
    /// Bootstrap-seeds PlatformAdministrator rows from the PlatformAdmin:AllowedEmails config
    /// allow-list (the same config key the pre-existing, now out-of-scope allow-list handlers
    /// elsewhere already read — this seeding does not touch or remove those checks). Idempotent
    /// and safe to call on every startup: for each configured email not already present
    /// (case-insensitive), creates an enabled PlatformOwner row with CreatedByUserId = null
    /// (system-seeded). Called from HR.Api's Program.cs startup sequence, in every environment,
    /// so the "platform:admin" policy (see PlatformAdminAuthorizationHandler) has real
    /// PlatformAdministrator rows to check against without a manual migration step.
    /// </summary>
    public static async Task SeedPlatformAdministratorsFromConfigAsync(
        this IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;

        var allowedEmails = configuration.GetSection("PlatformAdmin:AllowedEmails").Get<string[]>() ?? [];

        foreach (var email in allowedEmails)
        {
            if (string.IsNullOrWhiteSpace(email))
                continue;

            var normalizedEmail = email.Trim().ToLowerInvariant();
            var exists = await db.PlatformAdministrators.AnyAsync(a => a.Email == normalizedEmail);
            if (exists)
                continue;

            db.PlatformAdministrators.Add(PlatformAdministrator.Create(
                normalizedEmail, PlatformAdministratorRole.PlatformOwner, now, createdByUserId: null));
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Idempotently ensures a matching, login-ready Supabase Auth user AND a linked UserProfile row
    /// exist for every dev persona supplied (see HR.Api's DevPersonaStore.Personas), so the dev
    /// persona switcher can perform a real Supabase password-grant login for any of them and HR.Api
    /// can resolve their tenant (see SupabaseCurrentUserResolutionMiddleware/RequireTenantMiddleware
    /// — without a UserProfile row, an authenticated dev persona has no resolvable tenant and every
    /// request 403s). Called from HR.Api's IsDevelopment() startup seeding block, alongside
    /// SeedDevUserAsync above (which seeds the matching ApplicationUser/UserRole rows keyed by the
    /// same persona id). Identity cannot reference HR.Api's DevPersonaStore directly (host -> module
    /// dependency direction only), so the caller supplies the persona details to seed.
    /// </summary>
    public static async Task SeedDevSupabaseUsersAsync(
        this IServiceProvider services,
        IEnumerable<(Guid Id, Guid CompanyId, string Email, string FirstName, string LastName)> personas)
    {
        using var scope = services.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ISupabaseAuthGateway>();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var now = DateTimeOffset.UtcNow;

        foreach (var persona in personas)
        {
            var supabaseUserId = await gateway.EnsureDevUserAsync(
                persona.Email, SupabaseAuthGateway.DevSupabasePassword, CancellationToken.None);

            var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == persona.Id);
            if (profile is null)
            {
                db.UserProfiles.Add(UserProfile.Create(
                    persona.Id, supabaseUserId, persona.CompanyId, persona.Email,
                    persona.FirstName, persona.LastName, now));
            }
            else if (profile.SupabaseAuthUserId != supabaseUserId)
            {
                // Self-heal: an earlier seeding run's admin-list-users lookup (since replaced with a
                // password-grant sign-in — see SupabaseAuthGateway.EnsureDevUserAsync) could store a
                // SupabaseAuthUserId that doesn't match the "sub" claim actually issued on tokens,
                // permanently 404/403ing every request for that persona until corrected.
                profile.UpdateSupabaseAuthUserId(supabaseUserId, now);
            }
        }

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Idempotently ensures a single dev persona's Supabase Auth user AND linked UserProfile row
    /// exist (e.g. a brand-new self-service signup admin), using the same shared dev password as
    /// SeedDevSupabaseUsersAsync. See that method's remarks for why the UserProfile row matters.
    /// </summary>
    public static async Task EnsureDevSupabaseUserAsync(
        this IServiceProvider services,
        Guid id, Guid companyId, string email, string firstName, string lastName,
        CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ISupabaseAuthGateway>();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var supabaseUserId = await gateway.EnsureDevUserAsync(email, SupabaseAuthGateway.DevSupabasePassword, cancellationToken);

        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (profile is null)
        {
            db.UserProfiles.Add(UserProfile.Create(
                id, supabaseUserId, companyId, email, firstName, lastName, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (profile.SupabaseAuthUserId != supabaseUserId)
        {
            // A profile row can already exist here with a stale/fake SupabaseAuthUserId — e.g. a
            // self-service SignUp's UserProfile is created with whatever id CreateUserAsync
            // returned (a random Guid.NewGuid() under E2E's FakeSupabaseAuthGateway, which never
            // calls real Supabase), and this call is the first time a REAL Supabase user id is
            // obtained for that email. Without this self-heal (the same one
            // SeedDevSupabaseUsersAsync already applies for regular seeded personas), the stored
            // id never matches the "sub" claim on tokens actually issued for this account, and
            // every later login attempt fails to resolve a UserProfile despite Supabase itself
            // authenticating successfully.
            profile.UpdateSupabaseAuthUserId(supabaseUserId, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }

        // Baseline Employee role, same fallback AcceptInvite grants when an invite carries no
        // explicit role selection. Without this, a UserProfile created by this endpoint has zero
        // roles — LoginHandler now rejects zero-role accounts outright ("Invalid email or
        // password.", added to stop a broken blank-session login), so callers of this dev endpoint
        // (E2E tests logging in as a just-created employee) would otherwise never be able to log in
        // at all. Idempotent: only adds the role if it isn't already present.
        var hasAnyRole = await db.UserRoles.AnyAsync(ur => ur.UserId == id, cancellationToken);
        if (!hasAnyRole)
        {
            db.UserRoles.Add(UserRole.Create(id, SystemRoles.Employee, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Performs a real Supabase password-grant login for a dev persona's email, used by the dev
    /// persona switcher (HR.Api's /api/dev/persona/{userId} and /api/dev/persona/register) to
    /// establish a genuine Supabase session that HR.Web can turn into a session cookie. Returns a
    /// plain tuple rather than a module-defined type, since only IdentityModule itself may be a
    /// public exported type from this assembly (see
    /// IdentityModuleArchitectureTests.Identity_Module_Only_Exposes_Registration_Surface_As_Public).
    /// </summary>
    public static async Task<(string AccessToken, string RefreshToken, int ExpiresIn)> SignInDevPersonaAsync(
        this IServiceProvider services, string email, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ISupabaseAuthGateway>();
        var session = await gateway.SignInWithPasswordAsync(email, SupabaseAuthGateway.DevSupabasePassword, cancellationToken);
        var expiresIn = (int)Math.Max(1, (session.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        return (session.AccessToken, session.RefreshToken, expiresIn);
    }
}
