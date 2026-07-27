namespace HR.Web.Models;

public sealed record BackfillSourceResultModel(string Source, int Created, int Skipped, int Failed);

public sealed record TimelineBackfillResponse(
    Guid CompanyId,
    IReadOnlyList<BackfillSourceResultModel> Sources,
    int TotalCreated,
    int TotalSkipped,
    int TotalFailed);
