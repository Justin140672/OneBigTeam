using HR.Infrastructure.Abstractions;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Features.SignUp;

// Self-service signup: creates a brand-new Company (via the sanctioned ICompanyProvisioner
// cross-module contract, so Identity never references HR.Modules.Companies directly), seeds the
// default setup data a company needs (via ICompanyDefaultDataSeeder, implemented in
// HR.Modules.Employees), creates the admin's initial Employee record (via
// IEmployeeProvisioningService), then (Phase B) creates a real, pending Supabase Auth user via
// ISupabaseAuthGateway and a corresponding UserProfile — see CreateIdentityRecordAsync. This does
// NOT sign the admin in: the company stays PendingVerification and no session is established here.
// That only happens once the admin clicks the real verification email link (Phase D's VerifyEmail
// handler exchanges the code for a Supabase session and activates the company).
//
// Local-auth ApplicationUser creation (Phase A's stand-in for this step, SHA256 password hashing
// mirroring AcceptInvite) has been removed from this handler now that Phase B supersedes it for
// self-service signup. ApplicationUser itself is untouched and still used by AcceptInvite,
// DevAuthHandler, and seeded dev personas — this handler simply no longer creates one.
//
// Note: company provisioning (Companies schema), default data seeding + employee creation
// (Employees schema, and transitively Leave schema for the default leave policy), and identity
// record creation (Identity schema, plus a live call to Supabase) are committed as separate
// SaveChanges/calls — there is no cross-module distributed transaction mechanism in this
// architecture (each module owns its own DbContext/connection), and Supabase itself is an external
// system that cannot participate in a local transaction at all. If any step after company
// provisioning fails, the company is marked Deactivated as a best-effort compensation (not deleted —
// avoids FK cleanup complexity) and the failure is audited; this is intentionally not a full
// saga/outbox pattern, accepted debt given low signup volume, worth revisiting if this becomes a
// real reliability concern.
internal sealed class SignUpHandler(
    IdentityDbContext dbContext,
    ICompanyProvisioner companyProvisioner,
    ICompanyDefaultDataSeeder companyDefaultDataSeeder,
    IEmployeeProvisioningService employeeProvisioningService,
    ISupabaseAuthGateway supabaseAuthGateway,
    IAuditEventPublisher auditEventPublisher,
    IConfiguration configuration,
    IClock clock,
    ILogger<SignUpHandler> logger)
{
    public async Task<Result<SignUpResponse>> HandleAsync(SignUpRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.AdminEmail.Trim().ToUpperInvariant();

        // Checks both identity tables: ApplicationUser (local-auth path — AcceptInvite,
        // DevAuthHandler, seeded personas) and UserProfile (real Supabase-backed users, which is
        // what self-service SignUp itself creates as of Phase B) so a duplicate self-service signup
        // is caught regardless of which table the original account lives in.
        var emailInUse = await dbContext.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken)
            || await dbContext.UserProfiles.AnyAsync(p => p.Email.ToUpper() == normalizedEmail, cancellationToken);
        if (emailInUse)
        {
            return Result.Failure<SignUpResponse>(Error.Conflict("An account with this email already exists."));
        }

        var companyId = await companyProvisioner.ProvisionCompanyAsync(request.CompanyName.Trim(), cancellationToken);

        try
        {
            var defaults = await companyDefaultDataSeeder.SeedDefaultsAsync(companyId, cancellationToken);

            var employeeResult = await CreateAdminEmployeeAsync(companyId, defaults, request, cancellationToken);
            if (!employeeResult.IsSuccess)
            {
                throw new InvalidOperationException(employeeResult.Error.Message);
            }

            var user = await CreateIdentityRecordAsync(companyId, employeeResult.Value, request, cancellationToken);

            await auditEventPublisher.PublishAsync(
                new RegistrationCreatedAuditEvent(companyId, user.Id, clock.UtcNowOffset(), Succeeded: true, FailureReason: null),
                cancellationToken);

            var response = new SignUpResponse(user.Id, companyId, user.Email, user.FirstName, user.LastName);
            return Result.Success(response);
        }
        catch (EmailAlreadyRegisteredException ex)
        {
            // Supabase reported the email as already registered even though SignUpHandler's own
            // table check above didn't catch it (e.g. a Supabase user left over from a prior signup
            // attempt whose local UserProfile row never got created) — tell the customer the real
            // reason instead of falling into the generic "registration failed" message below.
            logger.LogWarning(ex, "Self-service registration failed for company {CompanyId}: email already registered with Supabase", companyId);
            await CompensateFailedRegistrationAsync(companyId, ex.Message, cancellationToken);
            return Result.Failure<SignUpResponse>(Error.Conflict("An account with this email already exists."));
        }
        catch (Exception ex)
        {
            // This is handled (not rethrown), so it never appears as an unhandled exception in
            // Aspire/console logs — logged explicitly here so a failed signup is actually
            // diagnosable. The full failure reason is also captured in the audit_events table via
            // RegistrationCreatedAuditEvent below, but that requires a DB query to see.
            logger.LogError(ex, "Self-service registration failed for company {CompanyId} after provisioning", companyId);
            await CompensateFailedRegistrationAsync(companyId, ex.Message, cancellationToken);
            return Result.Failure<SignUpResponse>(
                new Error("registration_failed", "Registration could not be completed. Please try again."));
        }
    }

    // Placeholder personal-detail values below: CreateEmployeeHandler's underlying Employee.Create
    // (called via IEmployeeProvisioningService, which bypasses the CreateEmployee FluentValidation
    // validator entirely — same as the existing candidate-hiring provisioning path) requires
    // DateOfBirth, Nationality, and Gender even though a self-service admin has supplied none of
    // this yet. These are exactly the kind of "employee record that gets edited later" values the
    // ticket anticipates — flagged as a known limitation rather than silently invented as if real.
    private async Task<Result<Guid>> CreateAdminEmployeeAsync(
        Guid companyId,
        CompanyDefaultDataResult defaults,
        SignUpRequest request,
        CancellationToken cancellationToken)
    {
        var provisioningRequest = new EmployeeProvisioningRequest(
            CompanyId: companyId,
            FirstName: request.AdminFirstName.Trim(),
            LastName: request.AdminLastName.Trim(),
            WorkEmail: request.AdminEmail.Trim(),
            StartDate: DateOnly.FromDateTime(clock.UtcNowOffset().Date),
            DateOfBirth: new DateOnly(1900, 1, 1),
            Nationality: "British",
            Gender: "Unknown",
            // CompanySettings.CreateDefault now defaults EmployeeNumberMode to Automatic, so
            // leaving this empty triggers CreateEmployeeHandler's auto-generation instead of
            // requiring a placeholder value.
            EmployeeNumber: string.Empty,
            EmploymentTypeId: defaults.EmploymentTypeId,
            DepartmentId: defaults.DepartmentId,
            LocationId: defaults.LocationId,
            PositionProfileId: defaults.PositionProfileId);

        return await employeeProvisioningService.CreateFromCandidateAsync(provisioningRequest, cancellationToken);
    }

    // Phase B: creates a real, pending Supabase Auth user (via ISupabaseAuthGateway.CreateUserAsync,
    // which sends the verification email) plus a corresponding local UserProfile, rather than a
    // local-auth ApplicationUser. The admin is NOT signed in here — the company remains
    // PendingVerification until Phase D's VerifyEmail flow runs.
    private async Task<UserProfile> CreateIdentityRecordAsync(
        Guid companyId, Guid employeeId, SignUpRequest request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var email = request.AdminEmail.Trim();

        var webBaseUrl =
            configuration["services:web:https:0"] ??
            configuration["services:web:http:0"] ??
            "http://localhost:5157";
        var redirectTo = $"{webBaseUrl}/verify-email/";

        var supabaseUserId = await supabaseAuthGateway.CreateUserAsync(email, request.Password, redirectTo, cancellationToken);

        // Use the employee ID as the user ID — single identity across modules, same convention
        // AcceptInvite already follows (see its own remarks). Confirmed via live diagnosis this
        // was previously generating an UNRELATED random id here instead, silently breaking that
        // invariant for every self-service signup: ListUsersHandler (User Administration),
        // EmployeeUserAccountStatusReader, and anything else joining on "employee id == user id"
        // could never find this admin's account at all.
        var profile = UserProfile.Create(
            employeeId,
            supabaseUserId,
            companyId,
            email,
            firstName: request.AdminFirstName.Trim(),
            lastName: request.AdminLastName.Trim(),
            now);
        dbContext.UserProfiles.Add(profile);

        // UserRole.UserId must equal UserProfile.Id (NOT the raw Supabase auth user id) — per
        // SupabaseCurrentUserResolutionMiddleware, ResolvedCurrentUser.UserId resolves to
        // profile.Id once a UserProfile row is found, and every authorization check downstream
        // (RoleAuthorizationHandler / IAuthorizationService.GetEffectiveRolesAsync) keys off
        // ICurrentUser.UserId. Every seeded persona carries SystemRoles.Employee alongside their
        // specific role — it's the floor role required by "role:employee", which gates core
        // session endpoints (GetMe, GetCompany, etc.) that AppSession depends on for every page.
        // Without it, a self-service admin would 403 on first load once verified.
        // The self-service admin is the company's first (and, at this point, only) user — without
        // HrAdministrator too they'd be locked out of Employees/HR Settings/User Administration
        // (and the Getting Started checklist would show tasks pointing at those pages that
        // immediately redirect them away, since it has no per-task role awareness — see
        // GettingStarted.razor/OnboardingTaskCard.razor). CompanyAdministrator alone was never
        // enough to actually use the app end to end.
        dbContext.UserRoles.Add(UserRole.Create(profile.Id, SystemRoles.Employee, now));
        dbContext.UserRoles.Add(UserRole.Create(profile.Id, SystemRoles.CompanyAdministrator, now));
        dbContext.UserRoles.Add(UserRole.Create(profile.Id, SystemRoles.HrAdministrator, now));

        await dbContext.SaveChangesAsync(cancellationToken);

        return profile;
    }

    private async Task CompensateFailedRegistrationAsync(Guid companyId, string failureReason, CancellationToken cancellationToken)
    {
        try
        {
            await companyProvisioner.DeactivateCompanyAsync(companyId, cancellationToken);
        }
        finally
        {
            await auditEventPublisher.PublishAsync(
                new RegistrationCreatedAuditEvent(companyId, AdminUserId: null, clock.UtcNowOffset(), Succeeded: false, failureReason),
                cancellationToken);
        }
    }
}
