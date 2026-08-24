namespace HR.Modules.Sickness.Features.DeactivateSicknessCategory;

internal sealed record DeactivateSicknessCategoryRequest
{
    public Guid CompanyId { get; init; }
    public Guid Id { get; init; }

    // SICK-06: populated by the endpoint from the authenticated user's resolved identity — never
    // bound from the client body.
    internal Guid? ActorEmployeeId { get; init; }
}
