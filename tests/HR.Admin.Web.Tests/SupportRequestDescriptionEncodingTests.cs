using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HR.Admin.Web.Tests;

/// <summary>
/// CodeQL #59 (cross-site scripting, flagged on the support request description): the admin
/// SupportRequestDetails.razor renders a customer-submitted description via Razor text
/// interpolation (<c>@_detail.Description</c>), which HTML-encodes, never via
/// <see cref="MarkupString"/>. (Only the separately-sanitised response body uses MarkupString.)
/// These tests pin the source invariant and prove the encoding behaviour without bUnit.
/// </summary>
public class SupportRequestDescriptionEncodingTests
{
    private const string MaliciousDescription = "<script>alert(1)</script><img src=x onerror=alert(2)>\"'&";

    [Fact]
    public void Razor_Source_Renders_Description_Via_Encoded_Text_Interpolation_Only()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "HR.Admin.Web", "Components", "Pages", "SupportRequestDetails.razor");
        Assert.True(File.Exists(path), $"Expected razor file at '{path}'.");
        var lines = File.ReadAllLines(path);

        Assert.Contains(lines, l => l.Contains("@_detail.Description", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l =>
            l.Contains("Description", StringComparison.Ordinal)
            && (l.Contains("MarkupString", StringComparison.Ordinal) || l.Contains("@((MarkupString)", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Text_Interpolation_Html_Encodes_Script_And_Markup()
    {
        var html = await RenderAsync(MaliciousDescription);

        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;img", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("<p>", html);
    }

    private static async Task<string> RenderAsync(string description)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<DescriptionProbe>(
                ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(DescriptionProbe.Description)] = description }));
            return output.ToHtmlString();
        });
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))
                && (dir.GetFiles("*.sln").Length > 0 || dir.GetFiles("*.slnx").Length > 0))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from " + AppContext.BaseDirectory);
    }

    /// <summary>Emits exactly what Razor compiles <c>&lt;p&gt;@_detail.Description&lt;/p&gt;</c> to.</summary>
    public sealed class DescriptionProbe : ComponentBase
    {
        [Parameter]
        public string? Description { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddContent(1, Description);
            builder.CloseElement();
        }
    }
}
