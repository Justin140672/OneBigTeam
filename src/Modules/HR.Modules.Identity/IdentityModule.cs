using Microsoft.Extensions.Logging;
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
using HR.Modules.Identity.Services.AccountEmailPolicy;
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

        if (isE2ETesting)
        {
            // In E2E mode, skip the Supabase Auth health check to avoid blocking startup when
            // test credentials are unavailable or the test environment can't reach Supabase.
            // E2E tests use a fake Supabase gateway and don't depend on real Auth connectivity.
            services.AddHealthChecks();
        }
        else
        {
            services.AddHealthChecks()
                .AddCheck<SupabaseAuthHealthCheck>("auth", tags: ["ready", "critical"]);
        }

        // Ticket 9: shared account-creation email-domain policy (public/disposable denylist). The
        // options validator re-loads the embedded denylist and validates every embedded and
        // configured entry at startup, so a missing/empty/malformed list stops the host instead of
        // silently allowing every address.
        services.AddOptions<AccountEmailDomainPolicyOptions>()
            .Bind(configuration.GetSection(AccountEmailDomainPolicyOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<Microsoft.Extensions.Options.IValidateOptions<AccountEmailDomainPolicyOptions>, AccountEmailDomainPolicyOptionsValidator>();
        services.AddSingleton<IAccountEmailDomainPolicy, AccountEmailDomainPolicy>();
        services.AddScoped<AccountCreationEmailGuard>();

        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        services.AddScoped<ICurrentTenant, HttpContextCurrentTenant>();
        services.AddScoped<HR.Modules.Identity.Authorization.ITargetUserCompanyGuard,
            HR.Modules.Identity.Authorization.TargetUserCompanyGuard>();
        services.AddScoped<HR.SharedKernel.IAuthorizationService, IdentityAuthorizationService>();
        services.AddScoped<HR.Modules.Identity.Authorization.LastActiveAdministratorGuard>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, RoleAuthorizationHandler>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PlatformAdminAuthorizationHandler>();
        services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<PermissionDenialAuditThrottle>();
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<IEmployeeUserAccountStatusReader, EmployeeUserAccountStatusReader>();
        services.AddScoped<IHrAdministratorDirectory, HrAdministratorDirectory>();
        services.AddScoped<ICompanyAdministratorDirectory, CompanyAdministratorDirectory>();
        services.AddScoped<IUserEmailReader, UserEmailReader>();
        services.AddScoped<ICompanyUserEmailSearchReader, CompanyUserEmailSearchReader>();
        services.AddScoped<ICompanyUserCountReader, CompanyUserCountReader>();

        services.AddScoped<IUserEmailDirectoryReader, UserEmailDirectoryReader>();

        services.AddScoped<IPlatformUserActivityReader, PlatformUserActivityReader>();

        services.AddScoped<ListUsersHandler>();
        services.AddScoped<IValidator<ListUsersRequest>, ListUsersValidator>();
        services.AddScoped<GetUserDetailsHandler>();
        services.AddScoped<IValidator<GetUserDetailsRequest>, GetUserDetailsValidator>();
        services.AddScoped<GetUserAuditHistoryHandler>();
        services.AddScoped<IValidator<GetUserAuditHistoryRequest>, GetUserAuditHistoryValidator>();
        services.AddScoped<GetEffectiveAccessHandler>();
        services.AddScoped<IValidator<GetEffectiveAccessRequest>, GetEffectiveAccessValidator>();

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

        services.AddScoped<HR.Modules.Identity.Features.QueueInvitationBatch.QueueInvitationBatchHandler>();
        services.AddScoped<IValidator<HR.Modules.Identity.Features.QueueInvitationBatch.QueueInvitationBatchRequest>,
            HR.Modules.Identity.Features.QueueInvitationBatch.QueueInvitationBatchValidator>();
        services.AddScoped<HR.Modules.Identity.Features.GetInvitationBatchStatus.GetInvitationBatchStatusHandler>();
        services.AddScoped<IValidator<HR.Modules.Identity.Features.GetInvitationBatchStatus.GetInvitationBatchStatusRequest>,
            HR.Modules.Identity.Features.GetInvitationBatchStatus.GetInvitationBatchStatusValidator>();
        services.AddScoped<HR.Modules.Identity.Features.GetLatestInvitationBatch.GetLatestInvitationBatchHandler>();
        services.AddScoped<IValidator<HR.Modules.Identity.Features.GetLatestInvitationBatch.GetLatestInvitationBatchRequest>,
            HR.Modules.Identity.Features.GetLatestInvitationBatch.GetLatestInvitationBatchValidator>();
        services.AddScoped<HR.Modules.Identity.Features.RetryInvitationBatch.RetryInvitationBatchHandler>();
        services.AddScoped<IValidator<HR.Modules.Identity.Features.RetryInvitationBatch.RetryInvitationBatchRequest>,
            HR.Modules.Identity.Features.RetryInvitationBatch.RetryInvitationBatchValidator>();
        services.AddScoped<Jobs.ProcessInvitationBatchJob>();
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

        // Ticket 13: cross-tab/cross-replica logout enforcement. See ISessionRevocationStore remarks.
        services.AddScoped<ISessionRevocationStore, SessionRevocationStore>();
        services.AddScoped<Features.Logout.LogoutHandler>();

        services.AddScoped<VerifyEmailHandler>();

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
        services.AddScoped<HR.Modules.Identity.Features.ActivatePlatformAdministrator.ActivatePlatformAdministratorHandler>();
        services.AddScoped<HR.Modules.Identity.Features.RetryPlatformAdministratorProvisioning.RetryPlatformAdministratorProvisioningHandler>();
        services.AddScoped<IValidator<ResetPlatformAdministratorMfaRequest>, ResetPlatformAdministratorMfaValidator>();

        services.AddScoped<
            IIntegrationEventHandler<EmployeeDepartureFinalisedIntegrationEvent>,
            Features.OnEmployeeDepartureFinalised.Handler>();
        services.AddScoped<Jobs.AccountDisablementJob>();
        // Ticket 10 (P1) follow-up: recovers stale/interrupted AccountDisablement requests (see
        // class docs) — registration was missed when the job was first added; without it, Hangfire's
        // activator cannot resolve the job type for its recurring schedule (see
        // UseIdentityRecurringJobs).
        services.AddScoped<Jobs.AccountDisablementReconciliationJob>();
        // Ticket 12 (P1): recovers InviteAcceptanceOperation rows stuck SupabaseConfirmed.
        services.AddScoped<Jobs.InviteAcceptanceReconciliationJob>();

        services.AddScoped<HR.Modules.Identity.Services.PositionSync>();
        // Ticket 6 follow-up: recurring, converge-style authoritative reconciliation (see class docs).
        services.AddScoped<HR.Modules.Identity.Services.PositionRoleReconciliationService>();
        services.AddScoped<Jobs.PositionRoleReconciliationJob>();
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

        services.AddScoped<ListEmployeeRoleOverridesHandler>();
        services.AddScoped<IValidator<ListEmployeeRoleOverridesRequest>, ListEmployeeRoleOverridesValidator>();
        services.AddScoped<AddEmployeeRoleOverrideHandler>();
        services.AddScoped<IValidator<AddEmployeeRoleOverrideRequest>, AddEmployeeRoleOverrideValidator>();
        services.AddScoped<RemoveEmployeeRoleOverrideHandler>();
        services.AddScoped<IValidator<RemoveEmployeeRoleOverrideRequest>, RemoveEmployeeRoleOverrideValidator>();
        services.AddScoped<ExpireEmployeeRoleOverridesJob>();
        services.AddScoped<Jobs.IdempotencyMaintenanceJob>();

        services.AddScoped<IWorkloadActionProvider, EmployeeAccountsAwaitingInvitationWorkloadActionProvider>();
        services.AddScoped<IWorkloadActionProvider, EmployeeAccountsAwaitingDisablementWorkloadActionProvider>();

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
        if (user is not null)
        {
            if (!user.IsActive)
                return false;

            user.RecordLogin(clock.UtcNow);
            await db.SaveChangesAsync();
            return true;
        }

        // Ticket 1 (P1): real Supabase-backed accounts (AcceptInvite, self-service SignUp) have no
        // ApplicationUser row at all — they must still be gated by their own UserProfile.IsActive,
        // otherwise a disabled invited/signed-up user could keep signing in indefinitely.
        var profile = await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == userId);
        if (profile is null)
            return true;

        return profile.IsActive;
    }

    public static IApplicationBuilder UseIdentityModule(this IApplicationBuilder app)
    {
        app.UseMiddleware<SupabaseCurrentUserResolutionMiddleware>();
        // Ticket 1: reject already-authenticated requests whose account has since been disabled
        // (manual disablement or automatic offboarding). Runs before RequireTenantMiddleware and
        // authorization so a disabled token cannot reach any protected handler.
        app.UseMiddleware<DisabledAccountMiddleware>();
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
        // P1: used exclusively by ActivatePlatformAdministrator. Deliberately just "authenticated"
        // — no role/permission/tenant requirement — because the whole point of that endpoint is to
        // grant the platform:admin-equivalent status for the first time; requiring it up front would
        // be circular. Company-agnostic in the same way platform:admin is (see its own remarks
        // immediately below), so it gets the identical RequireTenantMiddleware exemption.
        builder.AddPolicy("identity:self-provisioning", policy => policy
            .RequireAuthenticatedUser());

        builder.AddPolicy("platform:admin", policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new PlatformAdminRequirement()));

        builder.AddPolicy("role:employee",             RolePolicy(SystemRoles.Employee));
        builder.AddPolicy("role:manager",              RolePolicy(SystemRoles.Manager));
        builder.AddPolicy("role:recruiter",            RolePolicy(SystemRoles.Recruiter));
        builder.AddPolicy("role:hr-administrator",     RolePolicy(SystemRoles.HrAdministrator));
        builder.AddPolicy("role:company-administrator",RolePolicy(SystemRoles.CompanyAdministrator));

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

    public static WebApplication UseIdentityRecurringJobs(this WebApplication app)
    {
        var jobManager = app.Services.GetRequiredService<IRecurringJobManager>();
        jobManager.AddOrUpdate<ExpireEmployeeRoleOverridesJob>(
            "expire-employee-role-overrides",
            job => job.ExecuteAsync(),
            Cron.Daily(2));
        // Ticket 3 (P1) follow-up item 4: clean up expired idempotency records.
        jobManager.AddOrUpdate<Jobs.IdempotencyMaintenanceJob>(
            "identity-idempotency-maintenance",
            job => job.ExecuteAsync(),
            "*/5 * * * *");
        // Ticket 6 follow-up: recurring authoritative position-role reconciliation — recovers a
        // missed/failed EmployeePositionChangedIntegrationEvent sync without requiring a restart.
        // Documented recovery interval: within 15 minutes of the underlying data drifting.
        jobManager.AddOrUpdate<Jobs.PositionRoleReconciliationJob>(
            "identity-position-role-reconciliation",
            job => job.ExecuteAsync(),
            "*/15 * * * *");
        // Ticket 10 (P1) follow-up: recovers AccountDisablement requests stuck Pending/Processing/
        // Failed (interrupted enqueue, crashed job attempt) without requiring another departure event.
        jobManager.AddOrUpdate<Jobs.AccountDisablementReconciliationJob>(
            "identity-account-disablement-reconciliation",
            job => job.ExecuteAsync(),
            "*/10 * * * *");
        // Ticket 12 (P1): recovers InviteAcceptanceOperation rows stuck SupabaseConfirmed —
        // orphaning them (with an audit trail) when the underlying invite was cancelled while
        // provisioning was in flight, or converging them to Completed when the invite was actually
        // claimed. See InviteAcceptanceReconciliationJob for the full recovery scenarios.
        jobManager.AddOrUpdate<Jobs.InviteAcceptanceReconciliationJob>(
            "identity-invite-acceptance-reconciliation",
            job => job.ExecuteAsync(),
            "*/15 * * * *");
        return app;
    }

    /// <summary>
    /// Ticket 6 follow-up: authoritative, converge-style reconciliation of identity.user_positions —
    /// see <see cref="HR.Modules.Identity.Services.PositionRoleReconciliationService"/> for the
    /// actual logic. Originally an additive-only IAM-03 backfill (it only ever added a missing
    /// assignment, never expired a stale one, which left a failed revocation's grant active
    /// indefinitely); now converges every employee's assignments to their current authoritative
    /// position on every call. Called on every startup, in every environment, right after
    /// MigrateIdentityAsync for immediate first-run coverage, and again every 15 minutes via
    /// <see cref="Jobs.PositionRoleReconciliationJob"/> (see UseIdentityRecurringJobs) so a
    /// missed/failed sync recovers without requiring a restart.
    /// </summary>
    public static async Task ReconcilePositionRoleAssignmentsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var reconciliationService = scope.ServiceProvider
            .GetRequiredService<HR.Modules.Identity.Services.PositionRoleReconciliationService>();
        await reconciliationService.ReconcileAllCompaniesAsync(CancellationToken.None);
    }

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
            (Id: new Guid("30000000-0000-0000-0000-000000000018"), First: "Charlie", Last: "Wilson", Email: "charlie.wilson@betacorp.example", Roles: new[] { SystemRoles.Employee, SystemRoles.CompanyAdministrator }),
            (Id: new Guid("30000000-0000-0000-0000-000000000016"), First: "Olivia", Last: "Reyes",   Email: "olivia.reyes@acme.example",        Roles: new[] { SystemRoles.Employee, SystemRoles.HrAdministrator }),
            (Id: new Guid("30000000-0000-0000-0000-000000000017"), First: "Nina",   Last: "Patel",   Email: "nina.patel@acme.example",          Roles: new[] { SystemRoles.Employee, SystemRoles.Manager }),
            (Id: new Guid("30000000-0000-0000-0000-000000000019"), First: "Diana",  Last: "Chen",    Email: "diana.chen@gamma.example",         Roles: new[] { SystemRoles.Employee, SystemRoles.CompanyAdministrator }),
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

            var stale = await db.UserRoles
                .Where(ur => ur.UserId == persona.Id && !persona.Roles.Contains(ur.RoleId))
                .ToListAsync();
            db.UserRoles.RemoveRange(stale);
        }

        await db.SaveChangesAsync();
    }

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

    public static async Task SeedDevSupabaseUsersAsync(
        this IServiceProvider services,
        IEnumerable<(Guid Id, Guid CompanyId, string Email, string FirstName, string LastName)> personas)
    {
        using var scope = services.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ISupabaseAuthGateway>();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var logger = scope.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("HR.Modules.Identity.IdentityModule");
        var now = DateTimeOffset.UtcNow;

        foreach (var persona in personas)
        {
            try
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
                    profile.UpdateSupabaseAuthUserId(supabaseUserId, now);
                }

                // Ticket 9: converge a persona whose seeded login email changed (the Justin Etherington
                // persona moved from a Hotmail address to an organisation-style test domain) so an
                // existing dev database doesn't keep showing the old address on the profile.
                if (profile is not null && !string.Equals(profile.Email, persona.Email, StringComparison.OrdinalIgnoreCase))
                {
                    profile.UpdateEmail(persona.Email, now);
                }

                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex,
                    "Failed to seed dev Supabase user/profile for persona {PersonaId} — skipping, other personas unaffected.",
                    persona.Id);

                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State != Microsoft.EntityFrameworkCore.EntityState.Unchanged).ToList())
                    entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            }
        }
    }

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
            profile.UpdateSupabaseAuthUserId(supabaseUserId, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }

        var hasAnyRole = await db.UserRoles.AnyAsync(ur => ur.UserId == id, cancellationToken);
        if (!hasAnyRole)
        {
            db.UserRoles.Add(UserRole.Create(id, SystemRoles.Employee, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(cancellationToken);
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var userExists = await db.Users.AnyAsync(
            u => u.Id == id || u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (!userExists)
        {
            db.Users.Add(ApplicationUser.Create(
                id, email, passwordHash: "dev-only-not-used",
                firstName: firstName, lastName: lastName, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public static async Task<(string AccessToken, string RefreshToken, int ExpiresIn)> SignInDevPersonaAsync(
        this IServiceProvider services, string email, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var gateway = scope.ServiceProvider.GetRequiredService<ISupabaseAuthGateway>();
        var session = await gateway.SignInWithPasswordAsync(email, SupabaseAuthGateway.DevSupabasePassword, cancellationToken);
        var expiresIn = (int)Math.Max(1, (session.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds);
        return (session.AccessToken, session.RefreshToken, expiresIn);
    }

    /// <summary>
    /// Ticket 13: cross-tab/cross-replica logout enforcement. Called from HR.Api's
    /// SupabaseJwtBearerConfiguration.OnTokenValidated for every request whose bearer token has
    /// already passed full signature/issuer/audience/lifetime validation, on this app instance or
    /// any other replica sharing the same Postgres database. Returns true when
    /// <paramref name="tokenIssuedAt"/> (the token's own "iat" claim) is at or before the most recent
    /// recorded logout instant for <paramref name="supabaseAuthUserId"/> — i.e. this specific token
    /// was issued to a session that has since been logged out and must be rejected regardless of its
    /// own (unexpired) lifetime. <paramref name="services"/> is the current request's own
    /// IServiceProvider (JwtBearerEvents run inside the request's DI scope already), so this never
    /// creates a new scope. This is the only public entry point HR.Api (the host, one dependency
    /// direction removed from this module's internals) needs — everything else about session
    /// revocation stays internal to this module, matching every other cross-module/host boundary
    /// surface exposed from this class.
    ///
    /// Uses <c>GetService</c> (not <c>GetRequiredService</c>) deliberately: a handful of existing
    /// integration tests (e.g. SigningKeyRefreshResilienceTests) build a deliberately minimal host
    /// that wires up SupabaseJwtBearerConfiguration in isolation to exercise JWKS/signing-key
    /// behaviour, without registering this module's full DI graph. Those hosts are not exercising
    /// revocation at all, so this returns "not revoked" rather than throwing when the store isn't
    /// registered. HR.Api's real Program.cs always calls AddIdentityModule, which does register
    /// ISessionRevocationStore, so production behaviour is unaffected.
    /// </summary>
    public static async Task<bool> IsSessionRevokedAsync(
        this IServiceProvider services,
        Guid supabaseAuthUserId,
        DateTimeOffset tokenIssuedAt,
        CancellationToken cancellationToken)
    {
        var store = services.GetService<ISessionRevocationStore>();
        if (store is null)
        {
            return false;
        }

        var revokedAt = await store.GetRevokedAtAsync(supabaseAuthUserId, cancellationToken);
        return revokedAt is { } revoked && tokenIssuedAt <= revoked;
    }
}
