namespace HR.Modules.Employees.Domain;

internal sealed class PositionProfileRequiredDocument
{
    private PositionProfileRequiredDocument() { }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid PositionProfileId { get; private set; }
    public Guid DocumentTypeId { get; private set; }
    public bool IsMandatory { get; private set; }
    public int? DueDaysAfterStart { get; private set; }
    public bool RequiresExpiryDate { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }

    public static PositionProfileRequiredDocument Create(
        Guid id,
        Guid companyId,
        Guid positionProfileId,
        Guid documentTypeId,
        bool isMandatory,
        int? dueDaysAfterStart,
        bool requiresExpiryDate,
        Guid createdBy,
        DateTimeOffset now)
    {
        return new PositionProfileRequiredDocument
        {
            Id = id,
            CompanyId = companyId,
            PositionProfileId = positionProfileId,
            DocumentTypeId = documentTypeId,
            IsMandatory = isMandatory,
            DueDaysAfterStart = dueDaysAfterStart,
            RequiresExpiryDate = requiresExpiryDate,
            IsActive = true,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }
}
