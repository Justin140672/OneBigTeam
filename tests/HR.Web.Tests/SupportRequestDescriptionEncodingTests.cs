using System.Net;
using HR.SharedKernel;
using HR.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static HR.Web.Tests.ApiTestSupport;

namespace HR.Web.Tests;

public class SupportRequestDescriptionEncodingTests
{
    private const string MaliciousDescription = "<script>alert(1)</script><img src=x onerror=alert(2)>\"'&";

    [Fact]
    public void Razor_Source_Renders_Description_Via_Encoded_Text_Interpolation_Only()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "HR.Web", "Components", "Pages", "Support", "SupportRequestDetail.razor");
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
        Assert.StartsWith("<dd>", html);
    }

    [Fact]
    public async Task SubmitSupportRequestAsync_Sends_Description_As_Plain_Text_Form_Field_Verbatim()
    {
        var handler = new MultipartCapturingHandler();
        var service = new SupportService(BuildFactory(handler), NullLogger<SupportService>.Instance);

        var (result, error) = await service.SubmitSupportRequestAsync(
            Guid.NewGuid(), "ReportProblem", "Title", "  " + MaliciousDescription + "  ", "Medium", false,
            null, null, null, null, null, Array.Empty<IBrowserFile>());

        Assert.NotNull(result);
        Assert.Null(error);
        Assert.Equal(HttpMethod.Post, handler.Method);

        var part = Assert.Single(handler.Parts, p => p.Name == "Description");
        Assert.Equal(nameof(StringContent), part.ContentTypeName);
        Assert.Equal("text/plain", part.MediaType);
        Assert.Equal(FormText.Required("  " + MaliciousDescription + "  "), part.Value);
        Assert.Equal(MaliciousDescription, part.Value);
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

    public sealed class DescriptionProbe : ComponentBase
    {
        [Parameter]
        public string? Description { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "dd");
            builder.AddContent(1, Description);
            builder.CloseElement();
        }
    }

    private sealed record CapturedPart(string? Name, string ContentTypeName, string? MediaType, string Value);

    private sealed class MultipartCapturingHandler : HttpMessageHandler
    {
        public List<CapturedPart> Parts { get; } = [];
        public HttpMethod? Method { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            foreach (var part in multipart)
            {
                Parts.Add(new CapturedPart(
                    part.Headers.ContentDisposition?.Name?.Trim('"'),
                    part.GetType().Name,
                    part.Headers.ContentType?.MediaType,
                    await part.ReadAsStringAsync(cancellationToken)));
            }

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = System.Net.Http.Json.JsonContent.Create(new HR.Web.Models.SubmitSupportRequestResult(Guid.NewGuid(), "SR-0001")),
            };
        }
    }
}
