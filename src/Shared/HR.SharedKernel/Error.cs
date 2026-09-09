namespace HR.SharedKernel;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Validation(string message) => new("validation", message);

    public static Error NotFound(string message) => new("not_found", message);

    public static Error Conflict(string message) => new("conflict", message);

    // Optimistic-concurrency failure: the record changed in the database since the client loaded it.
    // Mapped to HTTP 409 like Conflict, but carries a distinct code so clients can offer a
    // reload-and-retry experience rather than treating it as a plain business-rule conflict.
    public static Error Concurrency(string message) => new("concurrency", message);

    public static Error Unauthorized(string message) => new("unauthorized", message);

    public static Error Forbidden(string message) => new("forbidden", message);

    public static Error Unexpected(string message) => new("unexpected", message);
}
