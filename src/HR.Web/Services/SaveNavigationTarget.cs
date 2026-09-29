namespace HR.Web.Services;

public static class SaveNavigationTarget
{
    public static bool IsSamePage(string? returnUrl, string landingUrl, string currentUri)
    {
        var target = string.IsNullOrWhiteSpace(returnUrl) ? landingUrl : returnUrl;
        return NormalisePath(target) == NormalisePath(currentUri);
    }

    private static string NormalisePath(string url)
    {
        var path = url.Trim();

        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
            path = absolute.AbsolutePath;

        var end = path.IndexOfAny(['?', '#']);
        if (end >= 0)
            path = path[..end];

        if (!path.StartsWith('/'))
            path = "/" + path;

        return path.Length > 1 ? path.TrimEnd('/').ToLowerInvariant() : path;
    }
}
