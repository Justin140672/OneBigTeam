using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Services.AccountEmailPolicy;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.AspNetCore.Http;
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
    AccountCreationEmailGuard accountCreationEmailGuard,
    IConfiguration configuration,
    IClock clock,
    ILogger<SignUpHandler> logger)
{
    public async Task<Result<SignUpResponse>> HandleAsync(SignUpRequest request, CancellationToken cancellationToken)
    {
        // Ticket 3 (P1) follow-up: dedupe a retried/duplicated signup submission BEFORE provisioning
        // a brand-new Company — this is the multi-step saga described in the class remarks above, so
        // catching a retry here (before ProvisionCompanyAsync ever runs again) is what actually
        // matters; the final SaveIdempotentAsync call in CreateIdentityRecordAsync only additionally
        // covers the narrow concurrent-race case (two identical requests landing at the same instant).
        // No CompanyId exists yet at this point (SignUp provisions a brand-new Company), so the
        // precheck scope uses Guid.Empty for CompanyId; the post-provisioning save in
        // CreateIdentityRecordAsync below scopes to the newly-created companyId instead.
        var scope = new IdempotencyScope(GetType().Name, Guid.Empty, Guid.Empty);

        var fingerprint = request.IdempotencyKey is not null
            ? DbContextIdempotencyExtensions.Fingerprint(request with { IdempotencyKey = null })
            : null;

        if (request.IdempotencyKey is { } precheckKey)
        {
            var replay = await dbContext.TryReplayAsync<IdempotencyRecord, SignUpResponse>(scope, precheckKey, fingerprint!, cancellationToken);

            switch (replay?.Kind)
            {
                case IdempotencyOutcomeKind.Replayed:
                    return Result.Success(replay.Response!);
                case IdempotencyOutcomeKind.KeyReused:
                    return Result.Failure<SignUpResponse>(
                        Error.Conflict("This Idempotency-Key was already used for a different request."));
            }
        }

        // Ticket 9: authoritative work-email check. Runs BEFORE the account-exists lookup (so the
        // response never reveals whether an account exists for a public address) and before ANY
        // provisioning side effect — no company, default data, employee, UserProfile, Supabase
        // account or verification email is created for a rejected address.
        var emailPolicy = await accountCreationEmailGuard.EnsureAllowedAsync(
            request.AdminEmail, AccountCreationPath.PublicSignup,
            companyId: Guid.Empty, subjectEmployeeId: null, actorUserId: null, cancellationToken);
        if (emailPolicy.IsFailure)
            return Result.Failure<SignUpResponse>(emailPolicy.Error);

        var normalizedEmail = request.AdminEmail.Trim().ToUpperInvariant();

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

            await employeeProvisioningService.MarkAsInitialCompanyAdminAsync(companyId, employeeResult.Value, cancellationToken);

            var (user, replayedResponse) = await CreateIdentityRecordAsync(
                companyId, employeeResult.Value, request, fingerprint, cancellationToken);

            if (replayedResponse is not null)
            {
                return Result.Success(replayedResponse);
            }

            await auditEventPublisher.PublishAsync(
                new RegistrationCreatedAuditEvent(companyId, user!.Id, clock.UtcNowOffset(), Succeeded: true, FailureReason: null),
                cancellationToken);

            var response = new SignUpResponse(user.Id, companyId, user.Email, user.FirstName, user.LastName);
            return Result.Success(response);
        }
        catch (EmailAlreadyRegisteredException ex)
        {
            logger.LogWarning("Self-service registration failed for company {CompanyId}: email already registered with Supabase", companyId);
            await CompensateFailedRegistrationAsync(companyId, ex.Message, cancellationToken);
            return Result.Failure<SignUpResponse>(Error.Conflict("An account with this email already exists."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Self-service registration failed for company {CompanyId} after provisioning", companyId);
            await CompensateFailedRegistrationAsync(companyId, ex.Message, cancellationToken);
            return Result.Failure<SignUpResponse>(
                new Error("registration_failed", "Registration could not be completed. Please try again."));
        }
    }

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
            EmployeeNumber: string.Empty,
            EmploymentTypeId: defaults.EmploymentTypeId,
            DepartmentId: defaults.DepartmentId,
            LocationId: defaults.LocationId,
            PositionProfileId: defaults.PositionProfileId);

        return await employeeProvisioningService.CreateFromCandidateAsync(provisioningRequest, cancellationToken);
    }

    private async Task<(UserProfile? Profile, SignUpResponse? ReplayedResponse)> CreateIdentityRecordAsync(
        Guid companyId, Guid employeeId, SignUpRequest request, string? fingerprint, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var email = request.AdminEmail.Trim();
        var scope = new IdempotencyScope(GetType().Name, companyId, Guid.Empty);

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

        dbContext.UserRoles.Add(UserRole.Create(profile.Id, SystemRoles.Employee, now));
        dbContext.UserRoles.Add(UserRole.Create(profile.Id, SystemRoles.CompanyAdministrator, now));
        dbContext.UserRoles.Add(UserRole.Create(profile.Id, SystemRoles.HrAdministrator, now));

        if (request.IdempotencyKey is { } key)
        {
            var precomputedResponse = new SignUpResponse(profile.Id, companyId, profile.Email, profile.FirstName, profile.LastName);
            var outcome = await dbContext.SaveIdempotentAsync<IdempotencyRecord, SignUpResponse>(
                dbContext.IdempotencyRecords, scope, key, fingerprint!, StatusCodes.Status200OK, precomputedResponse, now, cancellationToken);

            if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
                return (null, outcome.Response);
        }
        else
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return (profile, null);
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
