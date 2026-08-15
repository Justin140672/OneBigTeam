namespace HR.Modules.Companies.Features.RetryBackgroundJob;

internal sealed record RetryBackgroundJobRequest
{
    public string JobId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}
