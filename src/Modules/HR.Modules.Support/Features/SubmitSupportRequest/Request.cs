using HR.Modules.Support.Domain;
using Microsoft.AspNetCore.Http;

namespace HR.Modules.Support.Features.SubmitSupportRequest;

internal sealed record SubmitSupportRequestRequest
{
    public Guid CompanyId { get; init; }
    public SupportRequestType Type { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public SupportRequestPriority Priority { get; init; }
    public bool IncludeDiagnostics { get; init; } = true;
    public string? PageUrl { get; init; }
    public string? Browser { get; init; }
    public string? AppVersion { get; init; }
    public string? CorrelationId { get; init; }
    public List<string>? RecentClientErrors { get; init; }
    public IFormFileCollection? Files { get; init; }
}
