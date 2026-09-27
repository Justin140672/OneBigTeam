using HR.Marketing.Services;

namespace HR.Marketing.Tests;

public sealed class FeatureCatalogTests
{
    [Fact]
    public void Related_features_are_explicit_valid_and_unique()
    {
        var knownSlugs = FeatureCatalog.All.Select(feature => feature.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var feature in FeatureCatalog.All)
        {
            var related = FeatureCatalog.GetRelatedSlugs(feature.Slug);

            Assert.Equal(3, related.Count);
            Assert.Equal(related.Count, related.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.DoesNotContain(feature.Slug, related, StringComparer.OrdinalIgnoreCase);
            Assert.All(related, slug => Assert.Contains(slug, knownSlugs));
        }
    }

    [Fact]
    public void Recruitment_related_features_support_the_hiring_journey()
    {
        Assert.Equal(
            ["employee-management", "workflows-reminders", "reporting"],
            FeatureCatalog.GetRelatedSlugs("recruitment"));
    }
}
