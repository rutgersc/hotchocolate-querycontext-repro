using System.Linq.Expressions;
using GreenDonut.Data;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HotChocolateIncludeBugRepro;

public class IncludeDirectiveBugReproTests
{
    [Fact]
    public async Task QueryContext_Selector_Should_Respect_Include_False()
    {
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddFiltering()
            .AddSorting()
            .AddProjections()
            .AddQueryType<QueryContextQuery>()
            .BuildRequestExecutorAsync();

        QueryContextQuery.CapturedSelector = null;

        await executor.ExecuteAsync(
            """
            {
                items {
                    id
                    name @include(if: false)
                }
            }
            """);

        Assert.NotNull(QueryContextQuery.CapturedSelector);
        var selector = QueryContextQuery.CapturedSelector.ToString();

        Assert.Contains("Id", selector);
        Assert.DoesNotContain("Name", selector);
    }

    [Fact]
    public async Task QueryContext_Selector_Should_Respect_Skip_True()
    {
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddFiltering()
            .AddSorting()
            .AddProjections()
            .AddQueryType<QueryContextQuery>()
            .BuildRequestExecutorAsync();

        QueryContextQuery.CapturedSelector = null;

        await executor.ExecuteAsync(
            """
            {
                items {
                    id
                    name @skip(if: true)
                }
            }
            """);

        Assert.NotNull(QueryContextQuery.CapturedSelector);
        var selector = QueryContextQuery.CapturedSelector.ToString();

        Assert.Contains("Id", selector);
        Assert.DoesNotContain("Name", selector);
    }

    public class QueryContextQuery
    {
        public static Expression<Func<Item, Item>>? CapturedSelector { get; set; }

        public IQueryable<Item> GetItems(QueryContext<Item> context)
        {
            CapturedSelector = context.Selector;
            return new[] { new Item() }.AsQueryable().With(context);
        }
    }

    public class Item
    {
        public int Id { get; set; } = 1;
        public string Name { get; set; } = "Test";
    }
}
