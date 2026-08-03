using System.Net;
using System.Net.Http.Json;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

public class CompensationServiceTests
{
    private static IHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
    }

    [Fact]
    public async Task GetCurrentCompensationAsync_Returns_Model_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = new CurrentCompensationModel(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2026, 1, 1), null,
            "Annual", 45000m, 45000m, "GBP", 37.5m, 1m, null,
            "AnnualReview", Guid.NewGuid(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CompensationService(factory);

        var result = await service.GetCurrentCompensationAsync(companyId, employeeId);

        Assert.NotNull(result);
        Assert.Equal(45000m, result.Salary);
        Assert.Equal(45000m, result.AnnualisedSalary);
        Assert.Equal("Annual", result.SalaryType);
    }

    [Fact]
    public async Task GetCurrentCompensationAsync_Returns_Null_When_Api_Returns_NotFound()
    {
        var factory = BuildFactory(new StaticResponseHandler(HttpStatusCode.NotFound));
        var service = new CompensationService(factory);

        var result = await service.GetCurrentCompensationAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCurrentCompensationAsync_Returns_Null_On_Network_Failure()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new CompensationService(factory);

        var result = await service.GetCurrentCompensationAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetCompensationHistoryAsync_Returns_Items_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = new GetCompensationHistoryResponse(
        [
            new CompensationHistoryItemModel(Guid.NewGuid(), new DateOnly(2023, 1, 1), null, "Annual", 145000m, "GBP", 37.5m, 1m, "Promoted to CTO", "Promotion", Guid.NewGuid(), "Jane Doe", DateTimeOffset.UtcNow),
            new CompensationHistoryItemModel(Guid.NewGuid(), new DateOnly(2020, 1, 6), new DateOnly(2022, 12, 31), "Annual", 120000m, "GBP", 37.5m, 1m, "Starting salary", "NewHire", Guid.NewGuid(), "Jane Doe", DateTimeOffset.UtcNow)
        ]);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CompensationService(factory);

        var result = await service.GetCompensationHistoryAsync(companyId, employeeId);

        Assert.Equal(2, result.Count);
        Assert.Equal(145000m, result[0].Salary);
        Assert.Null(result[0].EffectiveTo);
        Assert.Equal(new DateOnly(2022, 12, 31), result[1].EffectiveTo);
    }

    [Fact]
    public async Task GetCompensationHistoryAsync_Returns_Empty_List_On_Network_Failure()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new CompensationService(factory);

        var result = await service.GetCompensationHistoryAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Empty(result);
    }

    [Fact]
    public async Task CreateCompensationRecordAsync_Returns_Result_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var response = new CreateCompensationRecordResponse(
            Guid.NewGuid(), companyId, employeeId, new DateOnly(2027, 1, 1), null,
            "Annual", 50000m, "GBP", null, null, null,
            "NewHire", Guid.NewGuid(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CompensationService(factory);

        var (result, error) = await service.CreateCompensationRecordAsync(
            companyId, employeeId,
            new CreateCompensationRecordRequest(companyId, employeeId, new DateOnly(2027, 1, 1), "Annual", 50000m, "GBP", null, null, null, "NewHire"));

        Assert.NotNull(result);
        Assert.Null(error);
        Assert.Equal(50000m, result.Salary);
    }

    [Fact]
    public async Task CreateCompensationRecordAsync_Returns_Error_On_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { Error = "Effective date overlaps with an existing compensation record." }));
        var service = new CompensationService(factory);

        var (result, error) = await service.CreateCompensationRecordAsync(
            Guid.NewGuid(), Guid.NewGuid(),
            new CreateCompensationRecordRequest(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2027, 1, 1), "Annual", 50000m, "GBP", null, null, null, "NewHire"));

        Assert.Null(result);
        Assert.Equal("Effective date overlaps with an existing compensation record.", error);
    }

    [Fact]
    public async Task UpdateFutureCompensationRecordAsync_Returns_Result_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var recordId = Guid.NewGuid();

        var response = new UpdateFutureCompensationRecordResponse(
            recordId, companyId, employeeId, new DateOnly(2027, 1, 1), null,
            "Hourly", 25m, "GBP", 20m, 0.5m, "Adjusted",
            "MarketAdjustment", Guid.NewGuid(),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CompensationService(factory);

        var (result, error) = await service.UpdateFutureCompensationRecordAsync(
            companyId, employeeId, recordId,
            new UpdateFutureCompensationRecordRequest(companyId, employeeId, recordId, "Hourly", 25m, "GBP", 20m, 0.5m, "Adjusted", "MarketAdjustment"));

        Assert.NotNull(result);
        Assert.Null(error);
        Assert.Equal("Hourly", result.SalaryType);
    }

    [Fact]
    public async Task UpdateFutureCompensationRecordAsync_Returns_Error_On_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { Error = "Only future-dated compensation records can be edited." }));
        var service = new CompensationService(factory);

        var (result, error) = await service.UpdateFutureCompensationRecordAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new UpdateFutureCompensationRecordRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Annual", 45000m, "GBP", null, null, null, "AnnualReview"));

        Assert.Null(result);
        Assert.Equal("Only future-dated compensation records can be edited.", error);
    }

    [Fact]
    public async Task DeleteFutureCompensationRecordAsync_Returns_Success_When_Api_Returns_NoContent()
    {
        var factory = BuildFactory(new StaticResponseHandler(HttpStatusCode.NoContent));
        var service = new CompensationService(factory);

        var (success, error) = await service.DeleteFutureCompensationRecordAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.True(success);
        Assert.Null(error);
    }

    [Fact]
    public async Task DeleteFutureCompensationRecordAsync_Returns_Error_On_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { Error = "A later compensation record exists for this employee; delete it first." }));
        var service = new CompensationService(factory);

        var (success, error) = await service.DeleteFutureCompensationRecordAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        Assert.False(success);
        Assert.Equal("A later compensation record exists for this employee; delete it first.", error);
    }

    [Fact]
    public async Task BulkApplyCompensationAdjustmentsAsync_Returns_Result_When_Api_Returns_Ok()
    {
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var bulkOperationId = Guid.NewGuid();

        var response = new BulkApplyCompensationAdjustmentsResponse(
            bulkOperationId,
            [new BulkCompensationAdjustmentResultItem(employeeId, Guid.NewGuid(), 40000m, 42000m, new DateOnly(2027, 1, 1))]);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CompensationService(factory);

        var (result, error) = await service.BulkApplyCompensationAdjustmentsAsync(
            companyId,
            new BulkApplyCompensationAdjustmentsRequest(
                companyId, new DateOnly(2027, 1, 1), "AnnualReview", null, "PercentageIncrease",
                [new BulkCompensationAdjustmentItem(employeeId, 42000m, "Annual", "GBP", null, null)]));

        Assert.NotNull(result);
        Assert.Null(error);
        Assert.Equal(bulkOperationId, result.BulkOperationId);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task BulkApplyCompensationAdjustmentsAsync_Returns_Error_On_Conflict()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.Conflict, new { Error = "Employee 'x': overlap conflict." }));
        var service = new CompensationService(factory);

        var (result, error) = await service.BulkApplyCompensationAdjustmentsAsync(
            Guid.NewGuid(),
            new BulkApplyCompensationAdjustmentsRequest(
                Guid.NewGuid(), new DateOnly(2027, 1, 1), "AnnualReview", null, "PercentageIncrease",
                [new BulkCompensationAdjustmentItem(Guid.NewGuid(), 42000m, "Annual", "GBP", null, null)]));

        Assert.Null(result);
        Assert.Equal("Employee 'x': overlap conflict.", error);
    }

    [Fact]
    public async Task DownloadImportTemplateAsync_Returns_Bytes_When_Api_Returns_Ok()
    {
        var expectedBytes = new byte[] { 1, 2, 3, 4 };
        var factory = BuildFactory(new ByteArrayResponseHandler(HttpStatusCode.OK, expectedBytes));
        var service = new CompensationService(factory);

        var (bytes, error) = await service.DownloadImportTemplateAsync(Guid.NewGuid());

        Assert.Null(error);
        Assert.Equal(expectedBytes, bytes);
    }

    [Fact]
    public async Task DownloadImportTemplateAsync_Returns_Error_On_Failure_Status()
    {
        var factory = BuildFactory(new StaticResponseHandler(HttpStatusCode.InternalServerError));
        var service = new CompensationService(factory);

        var (bytes, error) = await service.DownloadImportTemplateAsync(Guid.NewGuid());

        Assert.Null(bytes);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task DownloadImportTemplateAsync_Returns_Error_On_Network_Failure()
    {
        var factory = BuildFactory(new ThrowingHandler());
        var service = new CompensationService(factory);

        var (bytes, error) = await service.DownloadImportTemplateAsync(Guid.NewGuid());

        Assert.Null(bytes);
        Assert.NotNull(error);
    }

    [Fact]
    public async Task ImportCompensationChangesAsync_Returns_Result_When_Api_Returns_Ok()
    {
        var importBatchId = Guid.NewGuid();
        var response = new ImportCompensationChangesResponse(
            importBatchId,
            [new ImportedCompensationItem(Guid.NewGuid(), "EMP-001", Guid.NewGuid(), 46000m, new DateOnly(2027, 1, 1))]);

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.OK, response));
        var service = new CompensationService(factory);

        using var fileStream = new MemoryStream([1, 2, 3]);
        var (result, error, rowErrors) = await service.ImportCompensationChangesAsync(Guid.NewGuid(), fileStream, "import.xlsx");

        Assert.NotNull(result);
        Assert.Null(error);
        Assert.Null(rowErrors);
        Assert.Equal(importBatchId, result.ImportBatchId);
    }

    [Fact]
    public async Task ImportCompensationChangesAsync_Returns_RowErrors_On_UnprocessableEntity()
    {
        var rowErrorsPayload = new
        {
            Errors = new[] { new CompensationImportRowError(2, "New Salary must be a number greater than 0.") }
        };

        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.UnprocessableEntity, rowErrorsPayload));
        var service = new CompensationService(factory);

        using var fileStream = new MemoryStream([1, 2, 3]);
        var (result, error, rowErrors) = await service.ImportCompensationChangesAsync(Guid.NewGuid(), fileStream, "import.xlsx");

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.NotNull(rowErrors);
        Assert.Single(rowErrors);
        Assert.Equal(2, rowErrors[0].RowNumber);
    }

    [Fact]
    public async Task ImportCompensationChangesAsync_Returns_Error_On_BadRequest()
    {
        var factory = BuildFactory(new JsonResponseHandler(HttpStatusCode.BadRequest, new { Error = "Only .xlsx files are accepted." }));
        var service = new CompensationService(factory);

        using var fileStream = new MemoryStream([1, 2, 3]);
        var (result, error, rowErrors) = await service.ImportCompensationChangesAsync(Guid.NewGuid(), fileStream, "import.csv");

        Assert.Null(result);
        Assert.Equal("Only .xlsx files are accepted.", error);
        Assert.Null(rowErrors);
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

    private sealed class ByteArrayResponseHandler(HttpStatusCode statusCode, byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(bytes) };
            return Task.FromResult(response);
        }
    }

    private sealed class StaticResponseHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Network failure");
    }
}
