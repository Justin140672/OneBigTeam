using HR.Infrastructure.Abstractions;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Modules.Companies.Features.GetAuditLog;

/// <summary>
/// Platform Audit Log (Audit epic) — surfaces the audit trail already written by every existing
/// platform-administrator action (Subscription Management, Support, Job Monitoring epics) via the
/// existing cross-cutting IAuditHistoryReader/AuditDbContext mechanism. Deliberately does not
/// introduce a new audit table — this story is precisely about querying what's already recorded.
/// Same defense-in-depth allow-list gate as every other platform-wide Admin Portal handler (see
/// ListCustomersHandler's remarks) — no first-class platform-administrator identity model exists
/// yet, so the caller's email must additionally appear in "PlatformAdmin:AllowedEmails".
/// </summary>
internal sealed class GetAuditLogHandler(
    CompaniesDbContext dbContext,
    ICurrentUser currentUser,
    IConfiguration configuration,
    IAuditHistoryReader auditHistoryReader,
    IUserEmailDirectoryReader userEmailDirectoryReader)
{
    public async Task<Result<GetAuditLogResponse>> HandleAsync(
        GetAuditLogRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsAllowListedPlatformAdmin())
        {
            return Result.Failure<GetAuditLogResponse>(
                Error.Unauthorized("This account is not authorised to view platform-wide audit data."));
        }

        IReadOnlyCollection<Guid>? actorUserIds = null;
        if (!string.IsNullOrWhiteSpace(request.AdministratorEmail))
        {
            var matchedActorIds = await userEmailDirectoryReader.FindUserIdsByEmailAsync(
                request.AdministratorEmail, cancellationToken);

            // No administrator email matches at all — short-circuit to an empty page rather than
            // an unfiltered actorUserIds set (which GetPlatformAuditLogAsync treats as "no filter").
            if (matchedActorIds.Count == 0)
            {
                return Result.Success(new GetAuditLogResponse(
                    [], TotalCount: 0, request.PageNumber, request.PageSize, TotalPages: 0,
                    AuditLogActionTypes.All));
            }

            actorUserIds = matchedActorIds;
        }

        var pagination = new Pagination(request.PageNumber, request.PageSize);

        var page = await auditHistoryReader.GetPlatformAuditLogAsync(
            request.CompanyId,
            actorUserIds,
            request.FromDate,
            request.ToDate,
            request.EventType,
            pagination,
            cancellationToken);

        var companyIds = page.Items
            .Where(e => e.CompanyId != Guid.Empty)
            .Select(e => e.CompanyId)
            .Distinct()
            .ToList();

        var companyNamesById = companyIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.Companies
                .AsNoTracking()
                .Where(c => companyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var actorIds = page.Items
            .Where(e => e.ActorUserId.HasValue)
            .Select(e => e.ActorUserId!.Value)
            .Distinct()
            .ToList();

        var emailsByActorId = await userEmailDirectoryReader.GetEmailsByUserIdsAsync(actorIds, cancellationToken);

        var items = page.Items
            .Select(e => new AuditLogItem(
                e.OccurredAt,
                e.EventType,
                e.EntityType,
                e.CompanyId == Guid.Empty ? null : e.CompanyId,
                e.CompanyId != Guid.Empty && companyNamesById.TryGetValue(e.CompanyId, out var name) ? name : null,
                e.ActorUserId,
                e.ActorUserId.HasValue && emailsByActorId.TryGetValue(e.ActorUserId.Value, out var email) ? email : null,
                e.Summary))
            .ToList();

        return Result.Success(new GetAuditLogResponse(
            items, page.TotalCount, page.PageNumber, page.PageSize, page.TotalPages,
            AuditLogActionTypes.All));
    }

    private bool IsAllowListedPlatformAdmin()
    {
        var email = currentUser.Email;
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var allowedEmails = configuration.GetSection("PlatformAdmin:AllowedEmails").Get<string[]>()
            ?? [];

        return allowedEmails.Any(allowed =>
            string.Equals(allowed, email, StringComparison.OrdinalIgnoreCase));
    }
}
