namespace HR.Modules.Sickness.Features.UpdateSicknessCategory;

internal sealed record UpdateSicknessCategoryRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int DisplayOrder { get; init; }

    // SICK-06: populated by the endpoint from the authenticated user's resolved identity — never
    // bound from the client body.
    internal Guid? ActorEmployeeId { get; init; }
}
