using Microsoft.Extensions.Hosting;

namespace HR.Web.Tests;

/// <summary>
/// Ticket 5 — <see cref="ReleaseIdentity"/> is the running-release identity behind the anonymous
/// <c>GET /health/release</c> probe and the <c>release</c> block added to
/// <c>/health/startup-migrations</c>. The deploy pipeline relies on an EXACT sha match, so these pin
/// the sha/version resolution, the env-var overrides and the payload shape.
/// </summary>
[Collection("ReleaseIdentity env")]
public sealed class ReleaseIdentityTests : IDisposable
{
    private readonly string? _sha = Environment.GetEnvironmentVariable("RELEASE_SHA");
    private readonly string? _version = Environment.GetEnvironmentVariable("PLATFORM_VERSION");
    private readonly string? _service = Environment.GetEnvironmentVariable("RELEASE_SERVICE");
    private readonly string? _depId = Environment.GetEnvironmentVariable("RAILWAY_DEPLOYMENT_ID");
    private readonly string? _railSvc = Environment.GetEnvironmentVariable("RAILWAY_SERVICE_ID");
    private readonly string? _railEnv = Environment.GetEnvironmentVariable("RAILWAY_ENVIRONMENT_ID");
    private readonly string? _railCommit = Environment.GetEnvironmentVariable("RAILWAY_GIT_COMMIT_SHA");

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("RELEASE_SHA", _sha);
        Environment.SetEnvironmentVariable("PLATFORM_VERSION", _version);
        Environment.SetEnvironmentVariable("RELEASE_SERVICE", _service);
        Environment.SetEnvironmentVariable("RAILWAY_DEPLOYMENT_ID", _depId);
        Environment.SetEnvironmentVariable("RAILWAY_SERVICE_ID", _railSvc);
        Environment.SetEnvironmentVariable("RAILWAY_ENVIRONMENT_ID", _railEnv);
        Environment.SetEnvironmentVariable("RAILWAY_GIT_COMMIT_SHA", _railCommit);
    }

    [Theory]
    [InlineData("1.0.123+abc1234", null)]                                  // short build metadata only - not a full sha
    [InlineData("1.0.123", null)]                                          // no metadata
    [InlineData("1.0.123+0f3c1b2a9d4e5f60718293a4b5c6d7e8f9012345", "0f3c1b2a9d4e5f60718293a4b5c6d7e8f9012345")]
    [InlineData("1.0.123+abc1234.0f3c1b2a9d4e5f60718293a4b5c6d7e8f9012345", "0f3c1b2a9d4e5f60718293a4b5c6d7e8f9012345")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ShaFromInformationalVersion_extracts_only_a_real_commit(string? input, string? expected)
    {
        Assert.Equal(expected, ReleaseIdentity.ShaFromInformationalVersion(input));
    }

    [Fact]
    public void Sha_and_Version_prefer_env_overrides()
    {
        Environment.SetEnvironmentVariable("RELEASE_SHA", "  deadbeefdeadbeefdeadbeefdeadbeefdeadbeef  ");
        Environment.SetEnvironmentVariable("PLATFORM_VERSION", "9.9.9+override");

        Assert.Equal("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef", ReleaseIdentity.Sha());
        Assert.Equal("9.9.9+override", ReleaseIdentity.Version());
    }

    [Fact]
    public void Service_maps_known_hosts_and_honours_override()
    {
        Environment.SetEnvironmentVariable("RELEASE_SERVICE", null);
        Assert.Equal("api", ReleaseIdentity.Service("HR.Api"));
        Assert.Equal("app", ReleaseIdentity.Service("HR.Web"));
        Assert.Equal("marketing", ReleaseIdentity.Service("HR.Marketing"));
        Assert.Equal("admin", ReleaseIdentity.Service("HR.Admin.Web"));

        Environment.SetEnvironmentVariable("RELEASE_SERVICE", "custom-slug");
        Assert.Equal("custom-slug", ReleaseIdentity.Service("HR.Api"));
    }

    [Fact]
    public void Payload_exposes_only_the_safe_fields()
    {
        Environment.SetEnvironmentVariable("RELEASE_SHA", "abc1234abc1234abc1234abc1234abc1234abc123");
        Environment.SetEnvironmentVariable("PLATFORM_VERSION", "1.2.3");
        Environment.SetEnvironmentVariable("RAILWAY_DEPLOYMENT_ID", null);
        Environment.SetEnvironmentVariable("RAILWAY_SERVICE_ID", null);
        Environment.SetEnvironmentVariable("RAILWAY_ENVIRONMENT_ID", null);
        Environment.SetEnvironmentVariable("RAILWAY_GIT_COMMIT_SHA", null);

        var json = System.Text.Json.JsonSerializer.Serialize(
            ReleaseIdentity.Payload("Production", "HR.Api"));

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(
            new[] { "deploymentId", "environment", "railwayCommit", "railwayEnvironmentId", "railwayServiceId", "service", "sha", "startedAt", "version" },
            names);
        Assert.Equal("api", doc.RootElement.GetProperty("service").GetString());
        Assert.Equal("Production", doc.RootElement.GetProperty("environment").GetString());
        Assert.Equal("abc1234abc1234abc1234abc1234abc1234abc123", doc.RootElement.GetProperty("sha").GetString());
        // Railway-injected identity is null when the Railway env vars are absent (e.g. local/test).
        Assert.Equal(System.Text.Json.JsonValueKind.Null, doc.RootElement.GetProperty("deploymentId").ValueKind);
    }

    [Fact]
    public void Payload_reads_railway_injected_identity_from_railway_env_vars_not_release_sha()
    {
        Environment.SetEnvironmentVariable("RELEASE_SHA", "abc1234abc1234abc1234abc1234abc1234abc123");
        Environment.SetEnvironmentVariable("RAILWAY_DEPLOYMENT_ID", "dep-123");
        Environment.SetEnvironmentVariable("RAILWAY_SERVICE_ID", "svc-abc");
        Environment.SetEnvironmentVariable("RAILWAY_ENVIRONMENT_ID", "env-xyz");
        Environment.SetEnvironmentVariable("RAILWAY_GIT_COMMIT_SHA", "commitsha");

        var json = System.Text.Json.JsonSerializer.Serialize(ReleaseIdentity.Payload("Production", "HR.Api"));
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        Assert.Equal("dep-123", doc.RootElement.GetProperty("deploymentId").GetString());
        Assert.Equal("svc-abc", doc.RootElement.GetProperty("railwayServiceId").GetString());
        Assert.Equal("env-xyz", doc.RootElement.GetProperty("railwayEnvironmentId").GetString());
        Assert.Equal("commitsha", doc.RootElement.GetProperty("railwayCommit").GetString());
        // Distinct from the mutable display sha.
        Assert.NotEqual(doc.RootElement.GetProperty("sha").GetString(), doc.RootElement.GetProperty("deploymentId").GetString());
    }

    [Fact]
    public void ReleaseTag_is_just_sha_and_version()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ReleaseIdentity.ReleaseTag());
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var names = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "sha", "version" }, names);
    }
}

/// <summary>Serialises the env-var-mutating tests so they do not race.</summary>
[CollectionDefinition("ReleaseIdentity env", DisableParallelization = true)]
public sealed class ReleaseIdentityEnvCollection;
