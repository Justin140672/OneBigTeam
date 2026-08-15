namespace HR.Modules.Companies.Features.RetryBackgroundJob;

internal sealed record RetryBackgroundJobResponse(string JobId, bool Success);
