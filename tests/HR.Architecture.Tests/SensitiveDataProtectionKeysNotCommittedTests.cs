using System.Text.Json;

namespace HR.Architecture.Tests;

/// <summary>
/// Ticket 8 / NFR-01 — equality-monitoring answer columns are encrypted at rest with the keys in
/// <c>Infrastructure:SensitiveDataProtection:Keys</c>. Those keys are the only thing standing
/// between a leaked database dump and readable special-category data, so they must never be
/// committed to source control: they are supplied at runtime via environment / secret configuration
/// only (see <c>SensitiveDataProtectionOptions</c>). This test fails if any committed
/// <c>appsettings*.json</c> shipped with HR.Api carries a populated <c>Keys</c> map or a
/// non-empty <c>ActiveKeyId</c>.
/// </summary>
public class SensitiveDataProtectionKeysNotCommittedTests
{
    public static IEnumerable<object[]> ApiAppSettingsFiles()
    {
        var apiDir = Path.Combine(RepoRoot(), "src", "HR.Api");
        foreach (var file in Directory.EnumerateFiles(apiDir, "appsettings*.json"))
            yield return new object[] { file };
    }

    [Theory]
    [MemberData(nameof(ApiAppSettingsFiles))]
    public void ApiAppSettings_Does_Not_Contain_SensitiveDataProtection_Keys(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        if (!doc.RootElement.TryGetProperty("Infrastructure", out var infrastructure)
            || !infrastructure.TryGetProperty("SensitiveDataProtection", out var protection))
        {
            return; // section absent entirely — nothing committed, which is what we want.
        }

        if (protection.TryGetProperty("Keys", out var keys))
        {
            Assert.True(
                keys.ValueKind is JsonValueKind.Object && !keys.EnumerateObject().Any(),
                $"{Path.GetFileName(path)} commits Infrastructure:SensitiveDataProtection:Keys — "
                + "encryption keys must only come from environment / secret configuration.");
        }

        if (protection.TryGetProperty("ActiveKeyId", out var activeKeyId))
        {
            Assert.True(
                string.IsNullOrEmpty(activeKeyId.GetString()),
                $"{Path.GetFileName(path)} commits a non-empty Infrastructure:SensitiveDataProtection:ActiveKeyId.");
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "HR.Api", "appsettings.json")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
