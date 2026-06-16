namespace HR.Modules.Employees.Features.ListNationalities;

internal sealed record ListNationalitiesResponse(IReadOnlyList<NationalityItem> Items);

internal sealed record NationalityItem(int Id, string Name);
