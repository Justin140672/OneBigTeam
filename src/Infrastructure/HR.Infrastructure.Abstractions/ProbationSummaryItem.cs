namespace HR.Infrastructure.Abstractions;

public sealed record ProbationSummaryItem(
    string Status,
    DateOnly StartDate,
    DateOnly ExpectedEndDate,
    DateOnly? DecisionDate);
