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
            BodyHtml = bodyHtml,
            CreatedAt = now
        };
    }
}
