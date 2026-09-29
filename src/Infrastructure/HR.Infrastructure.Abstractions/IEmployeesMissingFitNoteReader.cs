namespace HR.Infrastructure.Abstractions;

public interface IEmployeesMissingFitNoteReader
{
    Task<IReadOnlySet<Guid>> GetEmployeeIdsMissingFitNotesAsync(
        Guid companyId,
        IEnumerable<Guid> employeeIds,
        CancellationToken cancellationToken);
}
