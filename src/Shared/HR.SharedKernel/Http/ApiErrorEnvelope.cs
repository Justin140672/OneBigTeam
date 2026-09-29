namespace HR.SharedKernel.Http;

public sealed record ApiErrorEnvelope(string? Error, string? Code = null);

public sealed record ApiValidationEnvelope(Dictionary<string, string[]>? Errors);
