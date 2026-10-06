# HotChocolate sort field dropped after nested object (QueryContext)

Reproduces a sort key being silently dropped from `QueryContext<T>.Sorting` when a sort field follows a
nested object field, e.g. `order: { category: { name: ASC }, position: ASC }`.

**HotChocolate version:** 16.5.1 (net9.0)

Related: ChilliCream/graphql-platform#8191 (same order shape, crashed in 15.1, closed by #8356),
#9111 (separate repro, branch `sort-bug`).

## Run

```
dotnet test
```

## Expected Output

4 tests fail, 4 pass.

| order argument | `QueryContext<T>`, `.With(ctx).ToList()` | no `QueryContext`, `[UseSorting]` on a returned `IQueryable<T>` |
| --- | --- | --- |
| `{ category: { name }, position }` | `position` dropped | both keys |
| `[{ category: { name } }, { position }]` | 2nd item dropped | both keys |
| `{ item: { category: { name }, position } }` (one level down) | `position` dropped | both keys |
| `{ position, category: { name } }` | both keys | not tested |
| `{ category: { name }, name }` | throws `Property 'Name' is not defined for type 'Item'` | not tested |

`QueryContext<T>` should sort the same as `[UseSorting]` on a returned `IQueryable<T>`, as in #9111.
