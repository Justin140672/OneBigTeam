namespace HR.Web.E2E.Tests.Infrastructure.PageObjects;

/// <summary>
/// Small helper for pulling an entity <see cref="System.Guid"/> out of an edit/view page URL such
/// as <c>/companies/{companyId}/candidates/{id}</c> or <c>/.../recruitment-stages/{id}/view</c> —
/// used by the concurrency-conflict tests, which create an entity through the list + "new" flow
/// and then need its id to drive two browser tabs against the same record.
/// </summary>
internal static class UrlIdParser
{
    /// <summary>Returns the last path segment of <paramref name="url"/> that parses as a GUID.</summary>
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
