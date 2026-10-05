using System.Diagnostics;
using System.Net;

namespace HR.Web.E2E.Tests.Infrastructure;

internal sealed class ReadinessTimeoutException(string message) : TimeoutException(message);

internal sealed class ReadinessProbeOptions
{
    public int BodyLimit { get; init; } = DiagnosticText.DefaultBodyLimit;
    public TimeSpan PollDelay { get; init; } = TimeSpan.FromSeconds(1);
    public int MigrationQueryEveryNAttempts { get; init; } = 5;
}

internal sealed class ReadinessProbe(
    HttpClient http,
    Action<string, string> log,
    Action<string, string> appendFile,
    ReadinessProbeOptions? options = null)
{
    private readonly ReadinessProbeOptions _options = options ?? new ReadinessProbeOptions();

    public async Task WaitUntilReadyAsync(
        string url,
        DateTime deadline,
        string? startupMigrationsUrl = null,
        CancellationToken cancellationToken = default)
    {
        var clock = Stopwatch.StartNew();
        var attempt = 0;
        HttpStatusCode? lastStatus = null;
        string? lastBody = null;
        string? lastError = null;
        string? migrations = null;
        string? previousSummary = null;

        while (DateTime.UtcNow < deadline)
        {
            attempt++;
            try
            {
                using var response = await http.GetAsync(url, cancellationToken);
                lastStatus = response.StatusCode;
                if (response.IsSuccessStatusCode) return;

                lastError = null;
                lastBody = await ReadBoundedBodyAsync(response, cancellationToken);
                previousSummary = Record(url, attempt, clock.Elapsed, $"status={(int)response.StatusCode}", lastBody, previousSummary);

                if (response.StatusCode == HttpStatusCode.ServiceUnavailable
                    && startupMigrationsUrl is not null
                    && (attempt == 1 || attempt % _options.MigrationQueryEveryNAttempts == 0))
                {
                    migrations = await QueryStartupMigrationsAsync(startupMigrationsUrl, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = $"{ex.GetType().Name}: {DiagnosticText.Sanitize(ex.Message, 300)}";
                lastBody = null;
                previousSummary = Record(url, attempt, clock.Elapsed, $"error={lastError}", null, previousSummary);
            }

            await Task.Delay(_options.PollDelay, cancellationToken);
        }

        if (lastStatus == HttpStatusCode.ServiceUnavailable && startupMigrationsUrl is not null)
        {
            migrations = await QueryStartupMigrationsAsync(startupMigrationsUrl, cancellationToken);
        }

        var message =
            $"Readiness probe never succeeded for {url} within the startup deadline "
            + $"after {attempt} attempt(s) in {clock.Elapsed.TotalSeconds:F1}s "
            + $"(last status: {lastStatus?.ToString() ?? "none"}, last error: {lastError ?? "none"}, "
            + $"last body: {(lastBody is null ? "none" : DiagnosticText.SingleLine(lastBody))})."
            + (migrations is null ? "" : $" Startup migrations: {DiagnosticText.SingleLine(migrations)}");
        throw new ReadinessTimeoutException(message);
    }

    private string Record(
        string url, int attempt, TimeSpan elapsed, string outcome, string? body, string? previousSummary)
    {
        var line = $"endpoint={url} attempt={attempt} elapsed={elapsed.TotalSeconds:F1}s {outcome}"
            + (body is null ? "" : $" body={DiagnosticText.SingleLine(body)}");
        appendFile("readiness-responses.log", line);

        var summary = outcome + body;
        if (summary != previousSummary || attempt % 10 == 0)
        {
            log("Readiness", line);
        }

        return summary;
    }

    private async Task<string> QueryStartupMigrationsAsync(string url, CancellationToken cancellationToken)
    {
        string result;
        try
        {
            using var response = await http.GetAsync(url, cancellationToken);
            var body = await ReadBoundedBodyAsync(response, cancellationToken);
            result = $"endpoint={url} status={(int)response.StatusCode} body={body}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = $"endpoint={url} unreachable: {ex.GetType().Name}: {DiagnosticText.Sanitize(ex.Message, 300)}";
        }

        result = DiagnosticText.SingleLine(result);
        appendFile("startup-migrations-responses.log", result);
        log("StartupMigrations", result);
        return result;
    }

    private async Task<string> ReadBoundedBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);
            var buffer = new char[_options.BodyLimit + 1];
            var read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
            var text = new string(buffer, 0, read);
            return DiagnosticText.Truncate(DiagnosticText.Redact(text), _options.BodyLimit, read == buffer.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return $"<body unreadable: {ex.GetType().Name}>";
        }
    }
}
