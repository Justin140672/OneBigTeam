using System.Diagnostics;
using System.Text.Json;
using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.Modules.Identity.Domain;
using HR.Modules.Identity.Persistence;
using HR.Modules.Identity.Services;
using HR.Modules.Identity.Services.AccountEmailPolicy;
using HR.Modules.Employees.Contracts;
using HR.SharedKernel;
using HR.SharedKernel.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Identity.Features.SignUp;

// Self-service signup, driven by a durable SignUpOperation (identity.signup_operations).
//
// The operation is claimed atomically (unique idempotency key, unique in-progress email) BEFORE any
// provisioning side effect, and advanced stage by stage as each independent commit lands:
//   claimed -> company_provisioned -> employee_created -> identity_created -> completed
// Company, default data and employee creation are idempotent against the operation's pre-allocated
// company id / source reference, and the Supabase account is stamped with the operation id, so a
// retry resumes from the persisted stage instead of re-provisioning. The final local commit
// (UserProfile + roles + replayable response) is a single transaction.
//
// Failures leave the operation resumable (lease released) until MaxAttempts is reached; abandoned
// operations are compensated (company deactivated, proven-owned Supabase account deleted) by the
// retrying request itself or by SignUpOperationReconciliationJob. Passwords and password-derived values are never persisted; the request fingerprint covers
// non-secret fields only.
internal sealed class SignUpHandler(
    IdentityDbContext dbContext,
    ICompanyProvisioner companyProvisioner,
    ICompanyDefaultDataSeeder companyDefaultDataSeeder,
    IEmployeeProvisioningService employeeProvisioningService,
    ISupabaseAuthGateway supabaseAuthGateway,
    IAuditEventPublisher auditEventPublisher,
    AccountCreationEmailGuard accountCreationEmailGuard,
    SignUpOperationCompensator compensator,
    IConfiguration configuration,
    IClock clock,
    ILogger<SignUpHandler> logger)
{
    internal const int MaxAttempts = 5;
    internal static readonly TimeSpan Lease = TimeSpan.FromMinutes(3);

    private sealed class SignUpConflictException : Exception;

    private sealed class SignUpLeaseLostException : Exception;

    private sealed record ClaimOutcome(SignUpOperation? Operation, bool OwnsLease, Result<SignUpResponse>? Early);

    public async Task<Result<SignUpResponse>> HandleAsync(SignUpRequest request, CancellationToken cancellationToken)
    {
        var fingerprint = SignUpIdempotencyMaterial.Fingerprint(request);
        var normalizedEmail = SignUpOperation.Normalize(request.AdminEmail);
        var key = request.IdempotencyKey;

        SignUpOperation? operation = null;
        var ownsLease = false;

        if (key is not null)
        {
            operation = await dbContext.SignUpOperations.SingleOrDefaultAsync(o => o.IdempotencyKey == key, cancellationToken);
            if (operation is not null && !operation.MatchesRequest(fingerprint, normalizedEmail))
            {
                return KeyReused();
            }
        }

        if (operation is null)
        {
            var preCheck = await ValidateNewRequestAsync(request, cancellationToken);
            if (preCheck is not null)
            {
                return preCheck;
            }

            var claim = await ClaimAsync(request, key, fingerprint, cancellationToken);
            if (claim.Early is not null)
            {
                return claim.Early;
            }

            operation = claim.Operation!;
            ownsLease = claim.OwnsLease;
            if (!operation.MatchesRequest(fingerprint, normalizedEmail))
            {
                return KeyReused();
            }

            if (ownsLease && await IsEmailInUseAsync(request, cancellationToken))
            {
                return await RejectClaimedOperationAsync(operation, cancellationToken);
            }
        }

        return await ContinueAsync(operation, request, ownsLease, cancellationToken);
    }

    private static Result<SignUpResponse> KeyReused() =>
        Result.Failure<SignUpResponse>(Error.Conflict("This Idempotency-Key was already used for a different request."));

    private async Task<Result<SignUpResponse>?> ValidateNewRequestAsync(SignUpRequest request, CancellationToken cancellationToken)
    {
        var emailPolicy = await accountCreationEmailGuard.EnsureAllowedAsync(
            request.AdminEmail, AccountCreationPath.PublicSignup,
            companyId: Guid.Empty, subjectEmployeeId: null, actorUserId: null, cancellationToken);
        if (emailPolicy.IsFailure)
            return Result.Failure<SignUpResponse>(emailPolicy.Error);

        if (WorkEmailAddressBuilder.ExtractDomain(request.AdminEmail.Trim()) is null)
            return Result.Failure<SignUpResponse>(Error.Validation("Enter a valid work email address."));

        if (await IsEmailInUseAsync(request, cancellationToken))
        {
            return Result.Failure<SignUpResponse>(Error.Conflict("An account with this email already exists."));
        }

        return null;
    }

    private async Task<bool> IsEmailInUseAsync(SignUpRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.AdminEmail.Trim().ToUpperInvariant();
        return await dbContext.UserProfiles.AnyAsync(p => p.Email.ToUpper() == normalizedEmail, cancellationToken);
    }

    private async Task<Result<SignUpResponse>> RejectClaimedOperationAsync(
        SignUpOperation operation, CancellationToken cancellationToken)
    {
        const string message = "An account with this email already exists.";
        var expectedVersion = operation.Version;
        operation.Fail("conflict", message, releaseKey: false, clock.UtcNowOffset());
        await dbContext.SaveChangesWithConcurrencyAsync(
            operation, expectedVersion, "Signup operation changed while it was rejected.", cancellationToken);
        return Result.Failure<SignUpResponse>(Error.Conflict(message));
    }

    private async Task<ClaimOutcome> ClaimAsync(
        SignUpRequest request, string? key, string fingerprint, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var email = request.AdminEmail.Trim();
        var normalized = SignUpOperation.Normalize(email);

        var competing = await dbContext.SignUpOperations
            .Where(o => o.NormalizedEmail == normalized && o.Status == SignUpOperation.StatusInProgress)
            .ToListAsync(cancellationToken);

        foreach (var stale in competing)
        {
            if (stale.LeaseIsActive(now))
            {
                return new ClaimOutcome(null, false, InProgress());
            }

            var expected = stale.Version;
            stale.TakeLease(now, Lease);
            var takeover = await dbContext.SaveChangesWithConcurrencyAsync(
                stale, expected, "Signup operation was claimed by another request.", cancellationToken);
            if (takeover.IsFailure)
            {
                return new ClaimOutcome(null, false, InProgress());
            }

            try
            {
                var compensated = await compensator.CompensateAsync(
                    stale, "registration_superseded",
                    "Registration could not be completed. Please try again.", releaseKey: true, cancellationToken);
                if (!compensated)
                {
                    return new ClaimOutcome(null, false, InProgress());
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not compensate abandoned signup operation {OperationId} before a new attempt.", stale.Id);
                await ReleaseLeaseAsync(stale, ex);
                return new ClaimOutcome(null, false, Failed());
            }
        }

        var operation = SignUpOperation.Claim(key, fingerprint, email, now, Lease);
        dbContext.SignUpOperations.Add(operation);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new ClaimOutcome(operation, true, null);
        }
        catch (DbUpdateException ex) when (PostgresUniqueViolation.Is(ex))
        {
            dbContext.ChangeTracker.Clear();

            if (key is not null)
            {
                var existing = await dbContext.SignUpOperations.SingleOrDefaultAsync(o => o.IdempotencyKey == key, cancellationToken);
                if (existing is not null)
                {
                    return new ClaimOutcome(existing, false, null);
                }
            }

            return new ClaimOutcome(null, false, InProgress());
        }
    }

    private static Result<SignUpResponse> InProgress() =>
        Result.Failure<SignUpResponse>(Error.Concurrency(
            "This registration is already being processed. Please wait a moment and try again."));

    private static Result<SignUpResponse> Failed() =>
        Result.Failure<SignUpResponse>(new Error("registration_failed", "Registration could not be completed. Please try again."));

    private async Task<Result<SignUpResponse>> ContinueAsync(
        SignUpOperation operation, SignUpRequest request, bool ownsLease, CancellationToken cancellationToken)
    {
        if (operation.Status == SignUpOperation.StatusCompleted)
        {
            return Result.Success(JsonSerializer.Deserialize<SignUpResponse>(operation.ResponseJson!)!);
        }

        if (operation.Status == SignUpOperation.StatusFailed)
        {
            return Result.Failure<SignUpResponse>(
                new Error(operation.FailureCode ?? "registration_failed", operation.FailureMessage ?? "Registration could not be completed."));
        }

        if (!ownsLease)
        {
            var now = clock.UtcNowOffset();
            if (operation.LeaseIsActive(now))
            {
                return await WaitForOutcomeAsync(operation.Id, cancellationToken);
            }

            var expected = operation.Version;
            operation.TakeLease(now, Lease);
            var takeover = await dbContext.SaveChangesWithConcurrencyAsync(
                operation, expected, "Signup operation was claimed by another request.", cancellationToken);
            if (takeover.IsFailure)
            {
                return await WaitForOutcomeAsync(operation.Id, cancellationToken);
            }
        }

        return await RunAsync(operation, request, cancellationToken);
    }

    private async Task<Result<SignUpResponse>> RunAsync(
        SignUpOperation operation, SignUpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (operation.AttemptCount > MaxAttempts)
            {
                return await CompensateAsync(operation, "registration_failed", releaseKey: true, cancellationToken);
            }

            return await ProvisionAsync(operation, request, cancellationToken);
        }
        catch (SignUpConflictException)
        {
            logger.LogWarning("Self-service registration {OperationId} failed: email already registered with Supabase.", operation.Id);
            return await CompensateAsync(operation, "conflict", releaseKey: false, cancellationToken);
        }
        catch (SignUpLeaseLostException)
        {
            return await WaitForOutcomeAsync(operation.Id, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ReleaseLeaseAsync(await ReloadAsync(operation), null);
            throw;
        }
        catch (Exception ex)
        {
            operation = await ReloadAsync(operation);
            logger.LogError(ex, "Self-service registration {OperationId} failed at stage {Stage} (attempt {Attempt}).",
                operation.Id, operation.Stage, operation.AttemptCount);

            if (operation.AttemptCount >= MaxAttempts)
            {
                try
                {
                    return await CompensateAsync(operation, "registration_failed", releaseKey: true, cancellationToken);
                }
                catch (Exception compensationEx) when (compensationEx is not OperationCanceledException)
                {
                    logger.LogError(compensationEx, "Compensation of signup operation {OperationId} failed; reconciliation will retry.", operation.Id);
                }
            }

            await ReleaseLeaseAsync(operation, ex);
            return Failed();
        }
    }

    private async Task<Result<SignUpResponse>> ProvisionAsync(
        SignUpOperation operation, SignUpRequest request, CancellationToken cancellationToken)
    {
        var adminAccount = new CompanyProvisioningAdmin(
            request.AdminEmail.Trim(), request.AdminFirstName.Trim(), request.AdminLastName.Trim());

        if (operation.Stage == SignUpOperation.StageClaimed)
        {
            await companyProvisioner.ProvisionCompanyAsync(
                request.CompanyName.Trim(), adminAccount, cancellationToken, operation.CompanyId);
            await AdvanceAsync(operation, o => o.MarkCompanyProvisioned(clock.UtcNowOffset()), cancellationToken);
        }

        if (operation.Stage == SignUpOperation.StageCompanyProvisioned)
        {
            var defaults = await companyDefaultDataSeeder.SeedDefaultsAsync(operation.CompanyId, cancellationToken);

            var employeeResult = await CreateAdminEmployeeAsync(operation, defaults, request, cancellationToken);
            if (!employeeResult.IsSuccess)
            {
                throw new InvalidOperationException(employeeResult.Error.Message);
            }

            await employeeProvisioningService.MarkAsInitialCompanyAdminAsync(operation.CompanyId, employeeResult.Value, cancellationToken);
            await AdvanceAsync(operation, o => o.MarkEmployeeCreated(employeeResult.Value, clock.UtcNowOffset()), cancellationToken);
        }

        if (operation.Stage == SignUpOperation.StageEmployeeCreated)
        {
            var supabaseUserId = await EnsureSupabaseUserAsync(operation, request, cancellationToken);
            await AdvanceAsync(operation, o => o.MarkIdentityCreated(supabaseUserId, clock.UtcNowOffset()), cancellationToken);
        }

        return await CompleteAsync(operation, request, cancellationToken);
    }

    private async Task<Result<Guid>> CreateAdminEmployeeAsync(
        SignUpOperation operation,
        CompanyDefaultDataResult defaults,
        SignUpRequest request,
        CancellationToken cancellationToken)
    {
        var provisioningRequest = new EmployeeProvisioningRequest(
            CompanyId: operation.CompanyId,
            FirstName: request.AdminFirstName.Trim(),
            LastName: request.AdminLastName.Trim(),
            WorkEmail: request.AdminEmail.Trim(),
            StartDate: DateOnly.FromDateTime(clock.UtcNowOffset().Date),
            DateOfBirth: new DateOnly(1900, 1, 1),
            Nationality: string.Empty,
            Gender: "Unknown",
            EmployeeNumber: string.Empty,
            EmploymentTypeId: defaults.EmploymentTypeId,
            DepartmentId: defaults.DepartmentId,
            LocationId: defaults.LocationId,
            PositionProfileId: defaults.PositionProfileId,
            SourceReference: $"signup:operation:{operation.Id}",
            IsInitialCompanyAdmin: true);

        return await employeeProvisioningService.CreateFromCandidateAsync(provisioningRequest, cancellationToken);
    }

    private async Task<Guid> EnsureSupabaseUserAsync(
        SignUpOperation operation, SignUpRequest request, CancellationToken cancellationToken)
    {
        var email = request.AdminEmail.Trim();
        var redirectTo = BuildRedirectUrl();
        var metadata = new Dictionary<string, string>
        {
            [SignUpOperation.ProvisioningMetadataKey] = operation.Id.ToString(),
        };

        try
        {
            return await supabaseAuthGateway.CreateUserAsync(email, request.Password, redirectTo, cancellationToken, metadata);
        }
        catch (EmailAlreadyRegisteredException)
        {
            var existing = await supabaseAuthGateway.GetUserMetadataByEmailAsync(email, cancellationToken);
            if (existing is { } found
                && found.Metadata.TryGetValue(SignUpOperation.ProvisioningMetadataKey, out var correlation)
                && correlation == operation.Id.ToString())
            {
                await supabaseAuthGateway.ResendVerificationEmailAsync(email, redirectTo, cancellationToken);
                return found.UserId;
            }

            throw new SignUpConflictException();
        }
    }

    private string BuildRedirectUrl()
    {
        var webBaseUrl =
            configuration["services:web:https:0"] ??
            configuration["services:web:http:0"] ??
            "http://localhost:5157";
        return $"{webBaseUrl}/verify-email/";
    }

    private async Task<Result<SignUpResponse>> CompleteAsync(
        SignUpOperation operation, SignUpRequest request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNowOffset();
        var email = request.AdminEmail.Trim();
        var employeeId = operation.EmployeeId!.Value;

        var profile = UserProfile.Create(
            employeeId,
            operation.SupabaseAuthUserId!.Value,
            operation.CompanyId,
            email,
            firstName: request.AdminFirstName.Trim(),
            lastName: request.AdminLastName.Trim(),
            now);
        dbContext.UserProfiles.Add(profile);

        dbContext.UserRoles.Add(UserRole.Create(profile.Id, SystemRoles.Employee, now));
        dbContext.UserRoles.Add(UserRole.Create(profile.Id, SystemRoles.CompanyAdministrator, now));
        dbContext.UserRoles.Add(UserRole.Create(profile.Id, SystemRoles.HrAdministrator, now));

        var response = new SignUpResponse(profile.Id, operation.CompanyId, profile.Email, profile.FirstName, profile.LastName);

        var expectedVersion = operation.Version;
        operation.Complete(JsonSerializer.Serialize(response), now);

        var save = await dbContext.SaveChangesWithConcurrencyAsync(
            operation, expectedVersion, "Signup operation changed during completion.", cancellationToken);
        if (save.IsFailure)
        {
            throw new SignUpLeaseLostException();
        }

        await auditEventPublisher.PublishAsync(
            new RegistrationCreatedAuditEvent(operation.CompanyId, profile.Id, now, Succeeded: true, FailureReason: null),
            cancellationToken);

        return Result.Success(response);
    }

    private async Task AdvanceAsync(
        SignUpOperation operation, Action<SignUpOperation> mutate, CancellationToken cancellationToken)
    {
        var expectedVersion = operation.Version;
        mutate(operation);
        operation.RenewLease(clock.UtcNowOffset(), Lease);

        var save = await dbContext.SaveChangesWithConcurrencyAsync(
            operation, expectedVersion, "Signup operation changed while it was being processed.", cancellationToken);
        if (save.IsFailure)
        {
            throw new SignUpLeaseLostException();
        }
    }

    private async Task<Result<SignUpResponse>> CompensateAsync(
        SignUpOperation operation, string code, bool releaseKey, CancellationToken cancellationToken)
    {
        var message = code == "conflict"
            ? "An account with this email already exists."
            : "Registration could not be completed. Please try again.";

        var done = await compensator.CompensateAsync(operation, code, message, releaseKey, cancellationToken);
        if (!done)
        {
            return InProgress();
        }

        return code == "conflict"
            ? Result.Failure<SignUpResponse>(Error.Conflict(message))
            : Failed();
    }

    private async Task<SignUpOperation> ReloadAsync(SignUpOperation operation)
    {
        dbContext.ChangeTracker.Clear();
        return await dbContext.SignUpOperations.SingleOrDefaultAsync(o => o.Id == operation.Id) ?? operation;
    }

    private async Task ReleaseLeaseAsync(SignUpOperation operation, Exception? cause)
    {
        try
        {
            var expected = operation.Version;
            operation.ReleaseLease(cause is null ? null : $"{operation.Stage}:{cause.GetType().Name}", clock.UtcNowOffset());
            await dbContext.SaveChangesWithConcurrencyAsync(
                operation, expected, "Signup operation changed while releasing its lease.", CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not release lease for signup operation {OperationId}.", operation.Id);
        }
    }

    private async Task<Result<SignUpResponse>> WaitForOutcomeAsync(Guid operationId, CancellationToken cancellationToken)
    {
        var waitSeconds = configuration.GetValue<int?>("SignUp:InProgressWaitSeconds") ?? 15;
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            var current = await dbContext.SignUpOperations.AsNoTracking()
                .SingleOrDefaultAsync(o => o.Id == operationId, cancellationToken);

            if (current is null)
            {
                return InProgress();
            }

            if (current.Status == SignUpOperation.StatusCompleted)
            {
                return Result.Success(JsonSerializer.Deserialize<SignUpResponse>(current.ResponseJson!)!);
            }

            if (current.Status == SignUpOperation.StatusFailed)
            {
                return current.FailureCode == "conflict"
                    ? Result.Failure<SignUpResponse>(Error.Conflict(current.FailureMessage ?? "An account with this email already exists."))
                    : Failed();
            }

            if (stopwatch.Elapsed.TotalSeconds >= waitSeconds)
            {
                return InProgress();
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }
}
