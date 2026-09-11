global using Microsoft.AspNetCore.Http;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Uses synthetic tokens and an in-memory HTTP transport. Never connects to an API.
await CheckAuth(false);
await CheckAuth(true);
await CheckRetry();
await LeaveProbes.Run();
await CheckImportPath();
await CheckEventFailure();

static async Task CheckImportPath()
{
    const string filename = "../../../review-probe.xlsx";
    var validator = new HR.Modules.DataImport.Services.ImportFileValidator(
        Microsoft.Extensions.Options.Options.Create(new HR.Modules.DataImport.Services.ImportFileUploadOptions()));
    var valid = validator.Validate(filename, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 100);
    var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "onebigteam", "data-import"));
    var key = $"{Guid.NewGuid()}/{Guid.NewGuid():N}/{filename}";
    // Production URL resolver uses the same ToFullPath as Upload/OpenRead/Delete. No file is written.
    var storage = new HR.Modules.DataImport.Services.LocalImportFileStorageService();
    var uri = await storage.GetDownloadUrlAsync(key, CancellationToken.None);
    var outside = !Path.GetFullPath(uri.LocalPath).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    Console.WriteLine($"IMPORT traversal filename accepted={valid.IsSuccess}; production storage path outside root={outside}; no file written");
    if (!valid.IsSuccess || !outside) throw new Exception("Import path observation changed: re-review finding.");
}

static async Task CheckEventFailure()
{
    var services = new ServiceCollection();
    services.AddLogging();
    var consumer = new ThrowingConsumer();
    services.AddSingleton<HR.SharedKernel.IIntegrationEventHandler<ProbeEvent>>(consumer);
    services.AddTransient<HR.SharedKernel.IntegrationEventPublisher>();
    using var provider = services.BuildServiceProvider();
    await provider.GetRequiredService<HR.SharedKernel.IntegrationEventPublisher>()
        .PublishAsync(new ProbeEvent(), CancellationToken.None);
    Console.WriteLine($"EVENT consumer throws; publisher returns success; calls={consumer.Calls}");
}

static async Task CheckAuth(bool admin)
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddHttpContextAccessor();
    var capture = new CaptureHandler();
    var client = services.AddHttpClient("hrapi", c => c.BaseAddress = new Uri("https://review.invalid"));
    if (admin)
    {
        services.AddScoped<HR.Admin.Web.Services.SupabaseSessionAccessor>();
        services.AddTransient<HR.Admin.Web.Services.SupabaseAuthDelegatingHandler>();
        client.AddHttpMessageHandler<HR.Admin.Web.Services.SupabaseAuthDelegatingHandler>();
    }
    else
    {
        services.AddScoped<HR.Web.Services.SupabaseSessionAccessor>();
        services.AddTransient<HR.Web.Services.SupabaseAuthDelegatingHandler>();
        client.AddHttpMessageHandler<HR.Web.Services.SupabaseAuthDelegatingHandler>();
    }
    client.ConfigurePrimaryHttpMessageHandler(() => capture);
    using var provider = services.BuildServiceProvider();
    var context = provider.GetRequiredService<IHttpContextAccessor>();
    var cookie = admin ? "obt_admin_supabase_at" : "obt_supabase_at";
    async Task Send(string? token)
    {
        context.HttpContext = new DefaultHttpContext();
        if (token is not null) context.HttpContext.Request.Headers.Cookie = $"{cookie}={token}";
        using var scope = provider.CreateScope();
        using var http = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("hrapi");
        using var response = await http.GetAsync("api/session");
    }
    await Send("synthetic-A");
    await Send("synthetic-B");
    await Send(null);
    context.HttpContext = null;
    using (var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("hrapi"))
    using (var response = await http.GetAsync("api/session")) { }
    var actual = string.Join(",", capture.Tokens.Select(x => x ?? "anonymous"));
    Console.WriteLine($"{(admin ? "ADMIN" : "WEB")} expected A,B,anonymous,anonymous; actual {actual}");
    var expectedBug = admin
        ? "synthetic-A,synthetic-A,synthetic-A,synthetic-A"
        : "synthetic-A,synthetic-B,synthetic-B,synthetic-B";
    if (actual != expectedBug) throw new Exception("Authentication observation changed: re-review finding.");
}

static async Task CheckRetry()
{
    var builder = Host.CreateApplicationBuilder();
    builder.AddServiceDefaults();
    var capture = new CaptureHandler { FailFirst = true };
    builder.Services.AddHttpClient("probe", c => c.BaseAddress = new Uri("https://review.invalid"))
        .ConfigurePrimaryHttpMessageHandler(() => capture);
    using var host = builder.Build();
    using var http = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("probe");
    using var response = await http.PostAsync("api/non-idempotent-mutation", new StringContent("synthetic-body"));
    Console.WriteLine($"RETRY one POST, first response 500 after simulated commit: {capture.Tokens.Count} sends; final {(int)response.StatusCode}");
    if (capture.Tokens.Count != 2) throw new Exception("Retry observation changed: re-review finding.");
}

sealed class CaptureHandler : HttpMessageHandler
{
    public List<string?> Tokens { get; } = [];
    public bool FailFirst { get; init; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Tokens.Add(request.Headers.Authorization?.Parameter);
        return Task.FromResult(new HttpResponseMessage(FailFirst && Tokens.Count == 1
            ? HttpStatusCode.InternalServerError : HttpStatusCode.OK));
    }
}

sealed record ProbeEvent : HR.SharedKernel.IIntegrationEvent;
sealed class ThrowingConsumer : HR.SharedKernel.IIntegrationEventHandler<ProbeEvent>
{
    public int Calls { get; private set; }
    public Task HandleAsync(ProbeEvent e, CancellationToken ct)
    {
        Calls++;
        throw new InvalidOperationException("Synthetic consumer failure");
    }
}
