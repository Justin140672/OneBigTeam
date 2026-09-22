using System.Text.RegularExpressions;

namespace HR.Architecture.Tests;

public class WorkflowActionRuntimeTests
{
    // GitHub retired the Node 20 runtime on hosted runners; these action majors only ship a Node 20 entrypoint.
    private static readonly Dictionary<string, string> KnownNode20OnlyVersions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["actions/checkout"] = "v4",
        ["actions/setup-dotnet"] = "v4",
        ["actions/upload-artifact"] = "v4",
        ["actions/download-artifact"] = "v4",
        ["actions/cache"] = "v3",
    };

    private static readonly Regex UsesPattern = new(@"uses:\s*([\w.-]+/[\w.-]+)@([\w.-]+)", RegexOptions.Compiled);

    private static string WorkflowsDirectory
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".github", "workflows")))
                dir = dir.Parent;

            return dir is null
                ? throw new DirectoryNotFoundException("Could not locate the repository's .github/workflows directory.")
                : Path.Combine(dir.FullName, ".github", "workflows");
        }
    }

    [Fact]
    public void No_workflow_references_a_known_Node20_only_action_version()
    {
        var violations = new List<string>();

        foreach (var file in Directory.GetFiles(WorkflowsDirectory, "*.yml", SearchOption.TopDirectoryOnly))
        {
            foreach (Match match in UsesPattern.Matches(File.ReadAllText(file)))
            {
                var action = match.Groups[1].Value;
                var version = match.Groups[2].Value;

                if (KnownNode20OnlyVersions.TryGetValue(action, out var blockedVersion) &&
                    string.Equals(version, blockedVersion, StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add($"{Path.GetFileName(file)}: {action}@{version}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "The following workflow steps use a Node 20-only action version, which no longer runs on hosted runners:\n" +
            string.Join('\n', violations));
    }
}
