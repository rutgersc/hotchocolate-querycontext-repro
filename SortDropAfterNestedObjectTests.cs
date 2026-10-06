using System.Text.Json;
using GreenDonut.Data;
using HotChocolate.Data.Sorting;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HotChocolateSortNullMemberRepro;

public class Item
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public Category Category { get; set; } = new();
}

public class Category
{
    public string Name { get; set; } = "";
}

public class Crate
{
    public int Id { get; set; }
    public Item Item { get; set; } = new();
}

file static class Data
{
    public static List<Item> Items { get; } =
    [
        new() { Id = 1, Name = "x", Position = 1, Category = new() { Name = "B" } },
        new() { Id = 2, Name = "x", Position = 3, Category = new() { Name = "A" } },
        new() { Id = 3, Name = "x", Position = 1, Category = new() { Name = "A" } },
        new() { Id = 4, Name = "x", Position = 2, Category = new() { Name = "A" } },
    ];

    public static List<Crate> Crates { get; } =
        Items.Select(i => new Crate { Id = i.Id, Item = i }).ToList();
}

public class QueryWithQueryContext
{
    public static QueryContext<Item>? CapturedItems;
    public static QueryContext<Crate>? CapturedCrates;

    [UseSorting]
    public IQueryable<Item> GetItems(QueryContext<Item> ctx)
    {
        CapturedItems = ctx;
        return Data.Items.AsQueryable().With(ctx);
    }

    [UseSorting]
    public IQueryable<Crate> GetCrates(QueryContext<Crate> ctx)
    {
        CapturedCrates = ctx;
        return Data.Crates.AsQueryable().With(ctx);
    }
}

public class QueryWithoutQueryContext
{
    [UseSorting]
    public IQueryable<Item> GetItems() => Data.Items.AsQueryable();

    [UseSorting]
    public IQueryable<Crate> GetCrates() => Data.Crates.AsQueryable();
}

/// <summary>
/// Sorting by a nested object field followed by a sibling field. With a bound
/// QueryContext&lt;T&gt; the sibling is dropped from the SortDefinition, while the same
/// order argument on an IQueryable&lt;T&gt; method sorts by both fields.
/// Data: Category A has Positions 3, 1, 2 (ids 2, 3, 4), Category B has id 1.
/// Sorting by category.name, then position gives ids 3, 4, 2, 1.
/// </summary>
public class SortDropAfterNestedObjectTests
{
    private static readonly int[] ByCategoryThenPosition = [3, 4, 2, 1];

    [Fact]
    public async Task QueryContext_NestedObjectThenScalar_KeepsBothSortKeys()
    {
        var (ids, error) = await Items<QueryWithQueryContext>(
            "{ category: { name: ASC }, position: ASC }");

        Assert.Null(error);
        Assert.Multiple(
            () => Assert.Equal(2, QueryWithQueryContext.CapturedItems!.Sorting!.Operations.Count()),
            () => Assert.Equal(ByCategoryThenPosition, ids));
    }

    [Fact]
    public async Task QueryContext_ScalarThenNestedObject_KeepsBothSortKeys()
    {
        var (ids, error) = await Items<QueryWithQueryContext>(
            "{ position: ASC, category: { name: ASC } }");

        Assert.Null(error);
        Assert.Multiple(
            () => Assert.Equal(2, QueryWithQueryContext.CapturedItems!.Sorting!.Operations.Count()),
            () => Assert.Equal([3, 1, 4, 2], ids));
    }

    [Fact]
    public async Task QueryContext_NestedObjectThenScalar_AsTwoListItems_KeepsBothSortKeys()
    {
        var (ids, error) = await Items<QueryWithQueryContext>(
            "[{ category: { name: ASC } }, { position: ASC }]");

        Assert.Null(error);
        Assert.Multiple(
            () => Assert.Equal(2, QueryWithQueryContext.CapturedItems!.Sorting!.Operations.Count()),
            () => Assert.Equal(ByCategoryThenPosition, ids));
    }

    [Fact]
    public async Task QueryContext_NestedObjectThenSameNamedScalar_DoesNotCrash()
    {
        var (_, error) = await Items<QueryWithQueryContext>(
            "{ category: { name: ASC }, name: ASC }");

        Assert.True(error is null, error);
    }

    [Fact]
    public async Task QueryContext_OneLevelDown_NestedObjectThenScalar_KeepsBothSortKeys()
    {
        var (ids, error) = await Crates<QueryWithQueryContext>(
            "{ item: { category: { name: ASC }, position: ASC } }");

        Assert.Null(error);
        Assert.Multiple(
            () => Assert.Equal(2, QueryWithQueryContext.CapturedCrates!.Sorting!.Operations.Count()),
            () => Assert.Equal(ByCategoryThenPosition, ids));
    }

    [Fact]
    public async Task WithoutQueryContext_NestedObjectThenScalar_SortsByBothKeys()
    {
        var (ids, error) = await Items<QueryWithoutQueryContext>(
            "{ category: { name: ASC }, position: ASC }");

        Assert.Null(error);
        Assert.Equal(ByCategoryThenPosition, ids);
    }

    [Fact]
    public async Task WithoutQueryContext_NestedObjectThenScalar_AsTwoListItems_SortsByBothKeys()
    {
        var (ids, error) = await Items<QueryWithoutQueryContext>(
            "[{ category: { name: ASC } }, { position: ASC }]");

        Assert.Null(error);
        Assert.Equal(ByCategoryThenPosition, ids);
    }

    [Fact]
    public async Task WithoutQueryContext_OneLevelDown_NestedObjectThenScalar_SortsByBothKeys()
    {
        var (ids, error) = await Crates<QueryWithoutQueryContext>(
            "{ item: { category: { name: ASC }, position: ASC } }");

        Assert.Null(error);
        Assert.Equal(ByCategoryThenPosition, ids);
    }

    private static Task<(int[] Ids, string? Error)> Items<TQuery>(string order) where TQuery : class
        => Run<TQuery>($"{{ items(order: {order}) {{ id }} }}", "items");

    private static Task<(int[] Ids, string? Error)> Crates<TQuery>(string order) where TQuery : class
        => Run<TQuery>($"{{ crates(order: {order}) {{ id }} }}", "crates");

    private static async Task<(int[] Ids, string? Error)> Run<TQuery>(string query, string field)
        where TQuery : class
    {
        var executor = await new ServiceCollection()
            .AddGraphQLServer()
            .AddQueryType<TQuery>()
            .AddSorting()
            .ModifyRequestOptions(o => o.IncludeExceptionDetails = true)
            .BuildRequestExecutorAsync();

        var result = await executor.ExecuteAsync(query);
        using var json = JsonDocument.Parse(result.ToJson());

        if (json.RootElement.TryGetProperty("errors", out var errors))
        {
            var exception = errors[0].GetProperty("extensions").GetProperty("exception");
            return ([], exception.GetProperty("message").GetString());
        }

        var ids = json.RootElement.GetProperty("data").GetProperty(field)
            .EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).ToArray();
        return (ids, null);
    }
}
