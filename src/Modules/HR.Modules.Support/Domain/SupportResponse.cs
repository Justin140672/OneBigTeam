using HR.SharedKernel.Html;

namespace HR.Modules.Support.Domain;

internal sealed class SupportResponse
{
    private SupportResponse() { }

    public Guid Id { get; private set; }
    public Guid SupportRequestId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public bool IsStaffResponse { get; private set; }
    public string BodyHtml { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// P1 stored-XSS fix: the body is ALWAYS passed through the shared support allow-list
    /// (<see cref="SupportHtmlSanitizer"/>) before it can be persisted, so every current and future
    /// write path (endpoints, seeders, tests) stores sanitised HTML. Untrusted markup must never
    /// reach <c>support.support_responses.body_html</c>.
    /// </summary>
    public static SupportResponse Create(
        Guid id,
        Guid supportRequestId,
        Guid companyId,
        Guid authorUserId,
        bool isStaffResponse,
        string bodyHtml,
        DateTimeOffset now)
    {
        return new SupportResponse
        {
            Id = id,
            SupportRequestId = supportRequestId,
            CompanyId = companyId,
            AuthorUserId = authorUserId,
            IsStaffResponse = isStaffResponse,
            BodyHtml = SupportHtmlSanitizer.Sanitize(bodyHtml),
            CreatedAt = now
        };
    }

    /// <summary>
    /// Re-applies the shared allow-list to a stored body (historical rows written before write-time
    /// sanitisation existed). Idempotent: returns <c>false</c> and changes nothing when the stored
    /// body is already sanitised.
    /// </summary>
    public bool ResanitiseBody()
    {
        var sanitised = SupportHtmlSanitizer.Sanitize(BodyHtml);
        if (string.Equals(sanitised, BodyHtml, StringComparison.Ordinal))
            return false;

        BodyHtml = sanitised;
        return true;
    }
}
