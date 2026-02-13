# HotChocolate Sort Field Null Member Reproduction

Reproduces a crash in `SortDefinitionFormatter` when a sort field is registered by string name
(no backing entity property) and `QueryContext<T>` is used.

**HotChocolate version:** 15.1.0

## Run

```
dotnet run
```

## Expected Output

Path 1 (SortVisitor, without `QueryContext<T>`) succeeds — the field is skipped.
Path 2 (SortDefinitionFormatter, with `QueryContext<T>`) crashes with "Unexpected Execution Error".
Both paths work fine with normal sort fields (e.g., `title`).

## Root Cause

`SortingContext.SortDefinitionFormatter.Enter` unconditionally dereferences `field.Member`:

```csharp
// SortingContext.cs:172
context.Parents.Push(Expression.Property(parent, (PropertyInfo)field.Member!));
```

`SortFieldDefinition.Member` is `MemberInfo?` (nullable). When a field is registered via
`descriptor.Field("custom")` (string name, not a lambda expression), `Member` remains `null`.

The `SortVisitor` path handles this gracefully — when the handler doesn't match
`ISortFieldHandler<TContext, T>`, it returns `SkipAndLeave`. But the `SortDefinitionFormatter`
has no such check and crashes.

## Expected Behavior

Both paths should behave consistently: if a field can't be processed, skip it.
The `SortDefinitionFormatter` should check for null `Member` and skip the field,
producing a `SortDefinition<T>` that only contains processable fields.
