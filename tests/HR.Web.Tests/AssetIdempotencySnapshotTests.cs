using System.Net;
using System.Net.Http.Json;
using HR.SharedKernel.Idempotency;
using HR.Web.Models;
using HR.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HR.Web.Tests;

/// <summary>
/// Bug fix (P1 follow-up to Ticket 19): pins the fixed behavior of
/// <see cref="EditPageBase{TModel, TKey}.SaveCoreAsync"/> fingerprinting
/// <see cref="IIdempotentCreateService{TModel}.BuildRequestSnapshot"/> (the exact normalized
/// request DTO that will actually be sent over HTTP) instead of the raw, un-normalized
/// <see cref="AssetEditModel"/>. Exercises <see cref="AssetService"/> as
/// <see cref="IIdempotentCreateService{AssetEditModel}"/> combined with a real
/// <see cref="PendingIdempotentOperation"/>, mirroring EditPageBase's actual call site.
/// </summary>
public class AssetIdempotencySnapshotTests
{
    private static HrApiHttpClientFactory BuildFactory(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("http://localhost/"))
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        return new HrApiHttpClientFactory(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), new CircuitSessionState());
    }

    private static IIdempotentCreateService<AssetEditModel> BuildService(out AssetService concrete)
    {
        concrete = new AssetService(BuildFactory(new NeverCalledHandler()));
        return concrete;
    }

    private static AssetEditModel BuildModel(
        string name = "Laptop", string? manufacturer = null, string? model = null, string? serialNumber = null) => new()
    {
        AssetNumber = "A-001",
        CategoryId = Guid.NewGuid(),
        Name = name,
        Manufacturer = manufacturer,
        Model = model,
        SerialNumber = serialNumber,
    };

    // 1. Whitespace-only edit that normalizes to the same required field must not rotate the key.
    [Fact]
    public void BuildRequestSnapshot_Whitespace_Only_Name_Edit_Produces_The_Same_Key()
    {
        var service = BuildService(out _);
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var model = BuildModel(name: " Laptop ");

        var first = operation.PrepareKey(service.BuildRequestSnapshot(companyId, model));

        model.Name = "Laptop";
        var second = operation.PrepareKey(service.BuildRequestSnapshot(companyId, model));

        Assert.Equal(first, second);
    }

    // 2. Optional field going from whitespace-only to null must not rotate the key (both normalize
    // to null via FormText.Optional).
    [Theory]
    [InlineData("Manufacturer")]
    [InlineData("Model")]
    [InlineData("SerialNumber")]
    public void BuildRequestSnapshot_Optional_Field_Whitespace_To_Null_Produces_The_Same_Key(string field)
    {
        var service = BuildService(out _);
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var model = BuildModel();
        SetOptionalField(model, field, "   ");

        var first = operation.PrepareKey(service.BuildRequestSnapshot(companyId, model));

        SetOptionalField(model, field, null);
        var second = operation.PrepareKey(service.BuildRequestSnapshot(companyId, model));

        Assert.Equal(first, second);
    }

    // 3. Empty string vs whitespace-only string for an optional field normalize identically.
    [Theory]
    [InlineData("Manufacturer")]
    [InlineData("Model")]
    [InlineData("SerialNumber")]
    public void BuildRequestSnapshot_Optional_Field_Empty_Vs_Whitespace_Produces_The_Same_Key(string field)
    {
        var service = BuildService(out _);
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var model = BuildModel();
        SetOptionalField(model, field, string.Empty);

        var first = operation.PrepareKey(service.BuildRequestSnapshot(companyId, model));

        SetOptionalField(model, field, "   ");
        var second = operation.PrepareKey(service.BuildRequestSnapshot(companyId, model));

        Assert.Equal(first, second);
    }

    // 4. A genuinely different name must still rotate the key.
    [Fact]
    public void BuildRequestSnapshot_Real_Name_Change_Produces_A_Different_Key()
    {
        var service = BuildService(out _);
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var model = BuildModel(name: "Laptop");

        var first = operation.PrepareKey(service.BuildRequestSnapshot(companyId, model));

        model.Name = "Desktop";
        var second = operation.PrepareKey(service.BuildRequestSnapshot(companyId, model));

        Assert.NotEqual(first, second);
    }

    // 5. Unchanged retry after an ambiguous outcome (operation never Completed) keeps the same key
    // when driven through BuildRequestSnapshot, mirroring
    // CreateAssetAsync_Ambiguous_Body_Read_Failure_Does_Not_Force_A_New_Key_On_Retry but exercised
    // via the fixed EditPageBase call site (BuildRequestSnapshot, not the raw model).
    [Fact]
    public async Task Ambiguous_Failure_Then_Unchanged_Retry_Reuses_The_Same_Key_Via_BuildRequestSnapshot()
    {
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var model = BuildModel();

        var capturing = new CapturingHandler(new ThrowingBodyResponseHandler(
            HttpStatusCode.Created, () => new IOException("Connection reset while reading response body.")));
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        IIdempotentCreateService<AssetEditModel> idempotentService = service;

        var firstKey = operation.PrepareKey(idempotentService.BuildRequestSnapshot(companyId, model));
        var outcome = await idempotentService.CreateAsync(companyId, model, firstKey);
        Assert.Equal(MutationOutcomeKind.AmbiguousFailure, outcome.Kind);
        // Ambiguous outcome: caller must not Complete() the operation.

        var secondKey = operation.PrepareKey(idempotentService.BuildRequestSnapshot(companyId, model));

        Assert.Equal(firstKey, secondKey);
    }

    // 6. A genuinely new submission after Complete() was called gets a fresh key even for an
    // identical-looking model.
    [Fact]
    public void Completed_Operation_Gets_A_Fresh_Key_For_An_Identical_Model()
    {
        var service = BuildService(out _);
        var operation = new PendingIdempotentOperation();
        var companyId = Guid.NewGuid();
        var model = BuildModel();

        var first = operation.PrepareKey(service.BuildRequestSnapshot(companyId, model));
        operation.Complete();

        var second = operation.PrepareKey(service.BuildRequestSnapshot(companyId, model));

        Assert.NotEqual(first, second);
    }

    // 7. BuildRequestSnapshot returns the exact same normalized CreateAssetRequest that is actually
    // POSTed over HTTP - proven by capturing the real HTTP body and comparing field-by-field against
    // the snapshot.
    [Fact]
    public async Task BuildRequestSnapshot_Matches_The_Request_Body_Actually_Sent_Over_HTTP()
    {
        var response = new CreateAssetResponse(
            Guid.NewGuid(), Guid.NewGuid(), "A-001", Guid.NewGuid(), "Laptop", null, null, null, null, null,
            "Available", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var capturing = new BodyCapturingHandler(new JsonResponseHandler<CreateAssetResponse>(response));
        var factory = BuildFactory(capturing);
        var service = new AssetService(factory);
        IIdempotentCreateService<AssetEditModel> idempotentService = service;

        var companyId = Guid.NewGuid();
        var model = BuildModel(name: " Laptop ", manufacturer: "   ", model: null, serialNumber: "SN-1");

        var snapshot = (CreateAssetRequest)idempotentService.BuildRequestSnapshot(companyId, model);
        await idempotentService.CreateAsync(companyId, model, Guid.NewGuid());

        Assert.NotNull(capturing.CapturedBody);
        var sentBody = System.Text.Json.JsonSerializer.Deserialize<CreateAssetRequest>(
            capturing.CapturedBody!, HrApiJsonOptions.Default);

        Assert.Equal(snapshot, sentBody);
        // Confirms the normalization actually happened, not just that both sides agree on a raw value.
        Assert.Equal("Laptop", snapshot.Name);
        Assert.Null(snapshot.Manufacturer);
    }

    private static void SetOptionalField(AssetEditModel model, string field, string? value)
    {
        switch (field)
        {
            case "Manufacturer": model.Manufacturer = value; break;
            case "Model": model.Model = value; break;
            case "SerialNumber": model.SerialNumber = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(field));
        }
    }

    // ── Fake handlers ────────────────────────────────────────────────────────────

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("BuildRequestSnapshot must not perform any HTTP call.");
    }

    private sealed class CapturingHandler(HttpMessageHandler inner) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var invoker = new HttpMessageInvoker(inner);
            return await invoker.SendAsync(request, cancellationToken);
        }
    }

    private sealed class BodyCapturingHandler(HttpMessageHandler inner) : HttpMessageHandler
    {
        public string? CapturedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);

            var invoker = new HttpMessageInvoker(inner);
            return await invoker.SendAsync(request, cancellationToken);
        }
    }

    private sealed class JsonResponseHandler<T>(T payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(payload) };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingBodyResponseHandler(HttpStatusCode statusCode, Func<Exception> exceptionFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode) { Content = new ThrowingContent(exceptionFactory) };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingContent(Func<Exception> exceptionFactory) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw exceptionFactory();

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            throw exceptionFactory();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
