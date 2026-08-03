using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class EmployeeNoteServiceTests
{
    private static IHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    [Fact]
    public async Task GetEmployeeNotesAsync_Returns_Items_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = new GetEmployeeNotesResponse(
        [
            new EmployeeNoteItemModel(Guid.NewGuid(), companyId, employeeId, "General", "Note text", false, false, null, Guid.NewGuid(), "Someone", DateTimeOffset.UtcNow)
        ]);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new EmployeeNoteService(factory);

        var result = await service.GetEmployeeNotesAsync(companyId, employeeId);

        Assert.Single(result);
        Assert.Equal("General", result[0].Category);
    }

    [Fact]
    public async Task GetEmployeeNotesAsync_Returns_Empty_List_On_Network_Failure()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new EmployeeNoteService(factory);

        var result = await service.GetEmployeeNotesAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Empty(result);
    }

    [Fact]
    public async Task CreateEmployeeNoteAsync_Returns_Result_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = new CreateEmployeeNoteResponse(
            Guid.NewGuid(), companyId, employeeId, "Performance", "Great work", true, false, null,
            Guid.NewGuid(), DateTimeOffset.UtcNow);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Created, response));
        var service = new EmployeeNoteService(factory);

        var (result, error) = await service.CreateEmployeeNoteAsync(
            companyId, employeeId,
            new CreateEmployeeNoteRequest(companyId, employeeId, "Performance", "Great work", true));

        Assert.NotNull(result);
        Assert.Null(error);
        Assert.Equal("Performance", result.Category);
    }

    [Fact]
    public async Task CreateEmployeeNoteAsync_Returns_Error_On_UnprocessableEntity()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.UnprocessableEntity, new { Error = "Note text is required." }));
        var service = new EmployeeNoteService(factory);

        var (result, error) = await service.CreateEmployeeNoteAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            new CreateEmployeeNoteRequest(Guid.NewGuid(), Guid.NewGuid(), "General", "", false));

        Assert.Null(result);
        Assert.Equal("Note text is required.", error);
    }

    [Fact]
    public async Task CreateEmployeeNoteAsync_Returns_Error_On_NotFound()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.NotFound, new { Error = "Employee not found." }));
        var service = new EmployeeNoteService(factory);

        var (result, error) = await service.CreateEmployeeNoteAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            new CreateEmployeeNoteRequest(Guid.NewGuid(), Guid.NewGuid(), "General", "Some note", false));

        Assert.Null(result);
        Assert.Equal("Employee not found.", error);
    }

    [Fact]
    public async Task SupersedeEmployeeNoteAsync_Returns_Result_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var originalNoteId = Guid.NewGuid();

        var response = new SupersedeEmployeeNoteResponse(
            Guid.NewGuid(), companyId, employeeId, "Conduct", "Updated note", false, false, null,
            Guid.NewGuid(), DateTimeOffset.UtcNow, originalNoteId, true);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Created, response));
        var service = new EmployeeNoteService(factory);

        var (result, error) = await service.SupersedeEmployeeNoteAsync(
            companyId, employeeId, originalNoteId,
            new SupersedeEmployeeNoteRequest(companyId, employeeId, "Conduct", "Updated note", false));

        Assert.NotNull(result);
        Assert.Null(error);
        Assert.Equal(originalNoteId, result.OriginalNoteId);
        Assert.True(result.OriginalNoteSuperseded);
    }

    [Fact]
    public async Task SupersedeEmployeeNoteAsync_Returns_Error_On_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { Error = "The original note has already been superseded." }));
        var service = new EmployeeNoteService(factory);

        var (result, error) = await service.SupersedeEmployeeNoteAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new SupersedeEmployeeNoteRequest(Guid.NewGuid(), Guid.NewGuid(), "General", "Updated note", false));

        Assert.Null(result);
        Assert.Equal("The original note has already been superseded.", error);
    }

    [Fact]
    public void GroupAndSort_Orders_Important_Notes_First_Then_Newest_First_Within_Each_Group()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var oldImportant = new EmployeeNoteItemModel(Guid.NewGuid(), companyId, employeeId, "General", "Old important", true, false, null, Guid.NewGuid(), "Someone", now.AddDays(-10));
        var newImportant = new EmployeeNoteItemModel(Guid.NewGuid(), companyId, employeeId, "General", "New important", true, false, null, Guid.NewGuid(), "Someone", now.AddDays(-1));
        var oldOther = new EmployeeNoteItemModel(Guid.NewGuid(), companyId, employeeId, "General", "Old other", false, false, null, Guid.NewGuid(), "Someone", now.AddDays(-5));
        var newOther = new EmployeeNoteItemModel(Guid.NewGuid(), companyId, employeeId, "General", "New other", false, false, null, Guid.NewGuid(), "Someone", now);

        var notes = new List<EmployeeNoteItemModel> { oldOther, oldImportant, newOther, newImportant };

        var sorted = EmployeeNoteService.GroupAndSort(notes);

        Assert.Equal(
        [
            newImportant.Id,
            oldImportant.Id,
            newOther.Id,
            oldOther.Id
        ], sorted.Select(n => n.Id));
    }

    // ── Category label helper ───────────────────────────────────────────────────

    [Theory]
    [InlineData("General")]
    [InlineData("Performance")]
    [InlineData("Attendance")]
    [InlineData("Conduct")]
    [InlineData("Wellbeing")]
    [InlineData("Recruitment")]
    [InlineData("Compensation")]
    [InlineData("Compliance")]
    [InlineData("Other")]
    public void EmployeeNoteCategories_Label_Maps_All_Known_Categories(string category)
    {
        Assert.Equal(category, EmployeeNoteCategories.Label(category));
    }

    [Fact]
    public void EmployeeNoteCategories_All_Contains_Exactly_Nine_Values()
    {
        Assert.Equal(9, EmployeeNoteCategories.All.Length);
    }

    // ── Fake handlers ────────────────────────────────────────────────────────────

    private sealed class JsonResponseHandler(HttpStatusCode statusCode, object payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode) { Content = JsonContent.Create(payload) };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network failure");
    }
}
