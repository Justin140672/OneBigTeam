using HR.Infrastructure.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HR.Modules.Recruitment.Services;

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
