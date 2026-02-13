using GreenDonut.Data;
using HotChocolate.Configuration;
using HotChocolate.Data.Sorting;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HotChocolateSortNullMemberRepro;

public class Book
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public List<BookMetadata> Metadata { get; set; } = [];
}

public class BookMetadata
{
    public int FieldId { get; set; }
    public string Value { get; set; } = "";
}

public class MetadataSortInputType : InputObjectType
{
    protected override void Configure(IInputObjectTypeDescriptor descriptor)
    {
        descriptor.Field("fieldId").Type<IntType>();
        descriptor.Field("direction").Type<DefaultSortEnumType>();
    }
}

/// <summary>
/// Custom handler that satisfies schema validation.
/// </summary>
public class SkipHandler : ISortFieldHandler
{
    public bool CanHandle(
        ITypeCompletionContext context,
        ISortInputTypeDefinition typeDefinition,
        ISortFieldDefinition fieldDefinition) => true;
}

public class BookSortType : SortInputType<Book>
{
    protected override void Configure(ISortInputTypeDescriptor<Book> descriptor)
    {
        descriptor.BindFieldsExplicitly();
        descriptor.Field(b => b.Title);
        // Can't use Field(b => b.Metadata) — throws "type of the member is unknown" for List<> properties.
        descriptor.Field("metadata")
            .Type<MetadataSortInputType>()
            .Extend()
            .OnBeforeCreate(d => d.Handler = new SkipHandler());
    }
}

file static class Books
{
    public static IQueryable<Book> All() => new List<Book>
    {
        new() { Id = 1, Title = "Zebra" },
        new() { Id = 2, Title = "Apple" },
    }.AsQueryable();
}

public class QueryWithoutQueryContext
{
    [UseSorting(typeof(BookSortType))]
    public IQueryable<Book> GetBooks() => Books.All();
}

public class QueryWithQueryContext
{
    [UseSorting(typeof(BookSortType))]
    public IQueryable<Book> GetBooks(QueryContext<Book> ctx) => Books.All().With(ctx);
}

/// <summary>
/// Sort field registered by string name with a custom InputObjectType child type
/// works with [UseSorting] but crashes with QueryContext.
/// </summary>
public class SortDefinitionFormatterTests
{
    [Fact]
    public async Task WithoutQueryContext_Succeeds()
    {
        var executor = await CreateExecutor<QueryWithoutQueryContext>();

        var json = await ExecuteToJson(executor,
            """{ books(order: [{ metadata: { fieldId: 42, direction: ASC } }]) { id title } }""");

        Assert.DoesNotContain("errors", json);
        Assert.Contains("books", json);
    }

    [Fact]
    public async Task WithQueryContext_Crashes()
    {
        var executor = await CreateExecutor<QueryWithQueryContext>();

        var json = await ExecuteToJson(executor,
            """{ books(order: [{ metadata: { fieldId: 42, direction: ASC } }]) { id title } }""");

        Assert.DoesNotContain("errors", json);
        Assert.Contains("books", json);
    }

    private static async Task<IRequestExecutor> CreateExecutor<TQuery>() where TQuery : class
        => await new ServiceCollection()
            .AddGraphQLServer()
            .AddQueryType<TQuery>()
            .AddSorting()
            .BuildRequestExecutorAsync();

    private static async Task<string> ExecuteToJson(IRequestExecutor executor, string query)
    {
        var result = await executor.ExecuteAsync(query);
        return result.ToJson();
    }
}
