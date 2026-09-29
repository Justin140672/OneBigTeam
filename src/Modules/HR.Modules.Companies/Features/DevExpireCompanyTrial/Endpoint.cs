using FastEndpoints;
using HR.Modules.Companies.Persistence;
using HR.SharedKernel;
using HR.SharedKernel.DevEndpoints;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Companies.Features.DevExpireCompanyTrial;

// Gate: Development environment AND E2E_TESTING=true, enforced by the shared [DevOnlyEndpoint]
// discovery filter + 404 pre-processor (HR.SharedKernel.DevEndpoints). Anonymous by design, like
// /api/dev/activate-company, so an E2E test can prepare a freshly signed-up company.
[DevOnlyEndpoint(DevEndpointKind.E2E)]
internal sealed class Endpoint(CompaniesDbContext db, IClock clock) : Endpoint<DevExpireCompanyTrialRequest>
{
    public override void Configure()
    {
        Post("/api/dev/expire-company-trial");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DevExpireCompanyTrialRequest request, CancellationToken cancellationToken)
    {
        var subscription = await db.CustomerSubscriptions
            .SingleOrDefaultAsync(s => s.CompanyId == request.CompanyId, cancellationToken);

        if (subscription is null)
        {
            await Send.NotFoundAsync(cancellationToken);
            return;
        }

        var now = clock.UtcNowOffset();
        db.Entry(subscription).Property(s => s.TrialExpiresAt).CurrentValue = now.AddMinutes(-1);
        subscription.MarkExpiredIfNeeded(now);
        await db.SaveChangesAsync(cancellationToken);

        await Send.NoContentAsync(cancellationToken);
    }
}
