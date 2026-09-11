namespace HR.Modules.Recruitment.Features.OfferCandidate;

internal sealed record OfferCandidateRequest
{
    public Guid CompanyId { get; init; }
    public Guid VacancyId { get; init; }
    public Guid ApplicationId { get; init; }

    // Ticket 2: the actual agreed offer terms. All optional on the wire:
    //  - OfferedSalary: when omitted, the handler pre-populates from the Position Profile's SalaryMin.
    //  - OfferedSalaryFrequency: "Annual" | "Hourly" | "Daily"; when omitted, taken from the Position
    //    Profile's SalaryType where available.
    //  - ProposedStartDate: the date the candidate is expected to start.
    //  - OfferDate: the date the offer is made; when omitted the handler defaults it to today.
    //  - OfferNotes: free-text context for the offer.
    public decimal? OfferedSalary { get; init; }
    public string? OfferedSalaryFrequency { get; init; }
    public DateOnly? ProposedStartDate { get; init; }
    public DateOnly? OfferDate { get; init; }
    public string? OfferNotes { get; init; }
}
