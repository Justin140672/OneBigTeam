using HR.Web.Components.Pages.Dashboards;

namespace HR.Web.Tests;

/// <summary>
/// DSH-03 — <see cref="WidgetResult{T}"/> is the DI-free per-source load outcome a dashboard widget
/// consumes. These pin the factory/status mapping and the guard on <see cref="WidgetResult{T}.ValueOrThrow"/>
/// (a Failed or Loading source must never be mistaken for an empty-but-loaded one).
/// </summary>
public class WidgetResultTests
{
    private const string Source = "Leave requests";

    [Fact]
    public void Loaded_ExposesValueAndStatusAndSource()
    {
        var result = WidgetResult<int>.Loaded(Source, 7);

        Assert.Equal(WidgetLoadStatus.Loaded, result.Status);
        Assert.True(result.IsLoaded);
        Assert.False(result.IsFailed);
        Assert.Equal(7, result.Value);
        Assert.Equal(7, result.ValueOrThrow);
        Assert.Equal(Source, result.SourceName);
    }

    [Fact]
    public void Loaded_PreservesNullReferenceValueWithoutBeingFailed()
    {
        var result = WidgetResult<string>.Loaded(Source, null!);

        Assert.True(result.IsLoaded);
        Assert.Null(result.ValueOrThrow);
    }

    [Fact]
    public void Failed_HasNoValueAndThrowsOnValueOrThrow()
    {
        var result = WidgetResult<int>.Failed(Source);

        Assert.Equal(WidgetLoadStatus.Failed, result.Status);
        Assert.True(result.IsFailed);
        Assert.False(result.IsLoaded);
        Assert.Equal(default, result.Value);
        Assert.Equal(Source, result.SourceName);

        var ex = Assert.Throws<InvalidOperationException>(() => result.ValueOrThrow);
        Assert.Contains(Source, ex.Message);
        Assert.Contains("Failed", ex.Message);
    }

    [Fact]
    public void Loading_IsNeitherLoadedNorFailedAndThrowsOnValueOrThrow()
    {
        var result = WidgetResult<int>.Loading(Source);

        Assert.Equal(WidgetLoadStatus.Loading, result.Status);
        Assert.False(result.IsLoaded);
        Assert.False(result.IsFailed);
        Assert.Equal(Source, result.SourceName);

        var ex = Assert.Throws<InvalidOperationException>(() => result.ValueOrThrow);
        Assert.Contains("Loading", ex.Message);
    }

    [Fact]
    public void SourceName_IsPreservedAcrossEveryFactory()
    {
        Assert.Equal(Source, WidgetResult<int>.Loading(Source).SourceName);
        Assert.Equal(Source, WidgetResult<int>.Loaded(Source, 1).SourceName);
        Assert.Equal(Source, WidgetResult<int>.Failed(Source).SourceName);
    }
}
