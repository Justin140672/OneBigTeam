using HR.Infrastructure.Abstractions;
using HR.Modules.Documents.Domain;
using HR.Modules.Documents.Features.GetDocumentRequest;
using HR.Modules.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Documents.Tests;

public class GetDocumentRequestHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

    private sealed class FakeEmployeeNameReader(Dictionary<Guid, string>? names = null) : IEmployeeNameReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
            Guid companyId, IEnumerable<Guid> employeeIds, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(names ?? new Dictionary<Guid, string>());
    }

    [Fact]
    public async Task HandleAsync_Returns_Request_With_DocumentTypeName_And_RequesterName_When_Found()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();

        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Passport", null, Now);
        context.DocumentTypes.Add(docType);

        var request = DocumentRequest.Create(
            Guid.NewGuid(), companyId, employeeId, docType.Id,
            null, DateOnly.FromDateTime(Now.Date).AddDays(30), true, null, requestedBy, Now);
        context.DocumentRequests.Add(request);
        await context.SaveChangesAsync();

        var nameReader = new FakeEmployeeNameReader(new Dictionary<Guid, string> { [requestedBy] = "Jane Manager" });
        var handler = new GetDocumentRequestHandler(context, nameReader);

        var result = await handler.HandleAsync(
            new GetDocumentRequestRequest { CompanyId = companyId, EmployeeId = employeeId, Id = request.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Passport", result.Value!.DocumentTypeName);
        Assert.Equal("Requested", result.Value.Status);
        Assert.Equal(requestedBy, result.Value.RequestedByEmployeeId);
        Assert.Equal("Jane Manager", result.Value.RequestedByName);
    }

    [Fact]
    public async Task HandleAsync_Returns_Null_RequestedByName_When_Not_Requested_By_Anyone()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Passport", null, Now);
        context.DocumentTypes.Add(docType);

        var request = DocumentRequest.Create(
            Guid.NewGuid(), companyId, employeeId, docType.Id,
            null, null, false, null, null, Now);
        context.DocumentRequests.Add(request);
        await context.SaveChangesAsync();

        var handler = new GetDocumentRequestHandler(context, new FakeEmployeeNameReader());
        var result = await handler.HandleAsync(
            new GetDocumentRequestRequest { CompanyId = companyId, EmployeeId = employeeId, Id = request.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.RequestedByEmployeeId);
        Assert.Null(result.Value.RequestedByName);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Does_Not_Exist()
    {
        await using var context = BuildContext();
        var handler = new GetDocumentRequestHandler(context, new FakeEmployeeNameReader());

        var result = await handler.HandleAsync(
            new GetDocumentRequestRequest { CompanyId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(), Id = Guid.NewGuid() },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_Returns_NotFound_When_Request_Belongs_To_Different_Employee()
    {
        await using var context = BuildContext();
        var companyId = Guid.NewGuid();

        var docType = DocumentType.Create(Guid.NewGuid(), companyId, "Passport", null, Now);
        context.DocumentTypes.Add(docType);

        var request = DocumentRequest.Create(
            Guid.NewGuid(), companyId, Guid.NewGuid(), docType.Id,
            null, null, false, null, null, Now);
        context.DocumentRequests.Add(request);
        await context.SaveChangesAsync();

        var handler = new GetDocumentRequestHandler(context, new FakeEmployeeNameReader());
        var result = await handler.HandleAsync(
            new GetDocumentRequestRequest { CompanyId = companyId, EmployeeId = Guid.NewGuid(), Id = request.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    private static DocumentsDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<DocumentsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
}
