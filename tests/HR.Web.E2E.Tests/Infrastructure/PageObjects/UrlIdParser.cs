namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

internal static class UrlIdParser
{
    public static System.Guid LastGuid(string url)
    {
        var path = new System.Uri(url).AbsolutePath.TrimEnd('/');
        var segments = path.Split('/');
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            if (System.Guid.TryParse(segments[i], out var id))
                return id;
        }

        throw new System.InvalidOperationException($"No GUID path segment found in URL: {url}");
    }
}
