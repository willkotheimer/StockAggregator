using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using StockAggregatorApp;
using Xunit;

namespace StockAggregator.Tests;

/// <summary>
/// A query parameter missing from the output-cache vary list is invisible: the
/// endpoint returns 200 with a stale body and the feature silently stops responding
/// to that parameter. This pins every [FromQuery] parameter on a cached controller
/// to <see cref="CachedQueryKeys.All"/> so the gap fails here instead of in prod.
/// </summary>
public class OutputCacheVaryTests
{
    private static IEnumerable<Type> CachedControllers() =>
        typeof(CachedQueryKeys).Assembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetCustomAttribute<OutputCacheAttribute>() is not null);

    private static IEnumerable<(string Controller, string Action, string Param)> QueryParameters() =>
        from controller in CachedControllers()
        from action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        from parameter in action.GetParameters()
        where parameter.GetCustomAttribute<FromQueryAttribute>() is not null
        select (controller.Name, action.Name, parameter.GetCustomAttribute<FromQueryAttribute>()!.Name ?? parameter.Name!);

    [Fact]
    public void EveryCachedQueryParameter_IsInTheVaryList()
    {
        var missing = QueryParameters()
            .Where(p => !CachedQueryKeys.All.Contains(p.Param, StringComparer.OrdinalIgnoreCase))
            .Select(p => $"{p.Controller}.{p.Action}({p.Param})")
            .Distinct()
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Query parameters on cached endpoints are missing from CachedQueryKeys.All, so the "
            + "output cache will serve one response for every value of them: "
            + string.Join(", ", missing));
    }

    [Fact]
    public void TheScanFindsControllers_SoAGreenResultMeansSomething()
    {
        // Guards the test itself: if the reflection ever stops matching, the assertion
        // above would pass vacuously.
        Assert.NotEmpty(CachedControllers());
        Assert.Contains(QueryParameters(), p => p.Param == "dow");
    }
}
