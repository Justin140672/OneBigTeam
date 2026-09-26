using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HR.Modules.Recruitment.Services;

/// <summary>
/// [P1] Fail-closed startup guard for candidate-document malware scanning. Recruitment does not choose
/// a scanner itself: it consumes the shared <see cref="IUploadedFileScanner"/> registered by
/// HR.Modules.Documents, whose registration already refuses to start Staging/Production without a
/// real (ClamAV) scanner — the no-op scanner is only permitted in Development or an explicit
/// automated-test environment. This check makes that dependency explicit for Recruitment: if the host
/// is ever composed without a resolvable scanner, it fails at startup rather than silently leaving
/// every uploaded CV Pending (or, worse, being wired to something that never inspects content).
/// </summary>
internal sealed class CandidateDocumentScannerStartupCheck(IServiceScopeFactory scopeFactory) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        if (scope.ServiceProvider.GetService<IUploadedFileScanner>() is null)
        {
            throw new InvalidOperationException(
                "Candidate document malware scanning is not configured: no IUploadedFileScanner is registered. "
                + "HR.Modules.Documents (AddDocumentsModule) must be registered with a real scanner "
                + "('Documents:ClamAv:Host') outside Development/automated-test environments.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
