using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Contracts;
using HR.SharedKernel;

namespace HR.Modules.Identity.Features.VerifyEmail;

internal sealed class VerifyEmailHandler(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    ICompanyProvisioner companyProvisioner,
    IAuditEventPublisher auditEventPublisher,
    IClock clock)
{
    public async Task<Result<VerifyEmailResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null || !Guid.TryParse(currentTenant.TenantId, out var companyId))
        {
            return Result.Failure<VerifyEmailResponse>(
                new Error("invalid_or_expired", "This verification link is invalid or has expired."));
        }

        var userId = currentUser.UserId.Value;

        var wasAlreadyActive = await companyProvisioner.IsCompanyActiveAsync(companyId, cancellationToken);

        if (!wasAlreadyActive)
        {
            await companyProvisioner.ActivateCompanyAsync(companyId, cancellationToken);

            var now = clock.UtcNowOffset();

            await auditEventPublisher.PublishAsync(
                new EmailVerificationSucceededAuditEvent(companyId, userId, now),
                cancellationToken);

            await auditEventPublisher.PublishAsync(
                new CompanyActivatedAuditEvent(companyId, now),
                cancellationToken);
        }

        return Result.Success(new VerifyEmailResponse(userId, companyId));
    }
}
