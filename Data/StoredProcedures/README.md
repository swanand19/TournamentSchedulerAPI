# Stored procedures

How the API calls a stored procedure. None exist yet; this is the pattern for the first one.

## Adding one

1. **Write the SQL** in `Data/StoredProcedures/Sql/usp_Name.sql` as `CREATE OR ALTER PROCEDURE`.
2. **Ship it in a migration.** Run `dotnet ef migrations add AddUspName`, then make its `Up` call
   `migrationBuilder.SqlFile("usp_Name.sql");` (and `Down` drop it).
3. **Describe it in C#.** Add a class in `Data/StoredProcedures/Procedures/`:

```csharp
[StoredProcedure("dbo.usp_GetCustomersByPhone", TimeoutSeconds = 30)]
public class GetCustomersByPhone : IQueryProcedure<Customer>        // what it returns
{
    [QueryParam] public string PhoneNumber { get; set; } = "";           // @PhoneNumber
    [QueryParam(Name = "max_rows")] public int? MaxRows { get; set; }    // @max_rows, NULL when null
    [QueryParam(ParameterDirection.Output)] public int TotalFound { get; set; }
    [QueryParam(ParameterDirection.Output, Size = 200)] public string? Message { get; set; }
    [QueryParam(Direction = ParameterDirection.ReturnValue)] public int ReturnCode { get; set; }
    [QueryParam(TableType = "dbo.IntList")] public DataTable? ExcludedIds { get; set; }
}
```

4. **Call it from a service** (never a controller) by injecting `IStoredProcedureExecutor`:

```csharp
var call = new GetCustomersByPhone { PhoneNumber = phone };
List<Customer> customers = await _procedures.QueryAsync(call);
int total = call.TotalFound;   // outputs are written back onto the object after the call
```

## Which method

| The class implements | Call | You get |
|---|---|---|
| `INonQueryProcedure` | `ExecuteAsync` | rows affected (-1 with `SET NOCOUNT ON`) |
| `IQueryProcedure<TRow>` | `QueryAsync` | every row of the first result set |
| `IQueryProcedure<TRow>` | `QuerySingleAsync` | the first row, or null |
| `IScalarProcedure<TValue>` | `ScalarAsync` | first column of the first row |

`TRow` is a class whose properties match the columns (by name, ignoring case, or `[ColumnName("...")]`),
or a simple type such as `int` or `string` for a one-column result.

## Rules the code enforces

- **Only `[QueryParam]` properties are sent.** Anything else on the class stays in C#.
- **Parameter types:** string, int, long, short, byte, bool, decimal, double, float, DateTime,
  DateTimeOffset, DateOnly, TimeOnly, TimeSpan, Guid, byte[], enums (sent as their number), and
  `DataTable` for a table type (input only, needs `TableType`). `null` is sent as `NULL`.
- **Outputs need their size:** string/byte[] outputs need `Size` (-1 for MAX), decimal outputs need
  `Precision` and `Scale` — otherwise SQL Server would silently truncate or round them.
- **NULL is never guessed.** A NULL arriving for an `int`, `bool` or `DateTime` property is an error
  naming the property. Make the property nullable (`int?`) when NULL is a real answer. Likewise a
  scalar with no value throws unless declared `IScalarProcedure<bool?>`.
- A class declared wrongly fails the first time it's used, with a message saying what to fix.

## Safety, integrity, performance

- Always `CommandType.StoredProcedure` with typed parameters; the name comes only from the attribute
  and is checked to be a procedure name. No text is ever spliced into SQL.
- Runs on the request's own connection. Inside `IUnitOfWork.BeginTransactionAsync()` a procedure
  and `SaveChangesAsync` commit or roll back together.
- Input strings are sent as `nvarchar(4000)` (or MAX when longer) rather than their exact length, so
  SQL Server reuses one cached plan instead of compiling one per length.
- Each procedure class and row type is inspected once and cached; values are read and written through
  compiled delegates, not reflection.
- Every method takes a `CancellationToken`; pass the request's so an abandoned request stops its query.
- A unique-index violation raises `DuplicateEntryException` (as a save does); any other database
  error raises `StoredProcedureException` with the procedure's name — never its parameter values.
