namespace TournamentScheduler.Api.Data.StoredProcedures;

/// <summary>
/// Runs stored procedures described by attributed classes.
///
/// <code>
/// [StoredProcedure("dbo.usp_GetCustomersByPhone")]
/// public class GetCustomersByPhone : IQueryProcedure&lt;Customer&gt;
/// {
///     [QueryParam] public string PhoneNumber { get; set; } = "";
///     [QueryParam(ParameterDirection.Output)] public int TotalFound { get; set; }
/// }
///
/// var call = new GetCustomersByPhone { PhoneNumber = phone };
/// List&lt;Customer&gt; customers = await _procedures.QueryAsync(call);
/// int total = call.TotalFound;   // outputs are written back onto the object
/// </code>
///
/// <para><b>Safety.</b> Always <c>CommandType.StoredProcedure</c> with typed parameters; the name
/// comes only from the attribute and is validated. No text is ever spliced into SQL.</para>
///
/// <para><b>Transactions.</b> Runs on the request's own connection. Inside
/// <see cref="IUnitOfWork.BeginTransactionAsync"/> the procedure joins that transaction, so it
/// commits or rolls back together with the entity changes saved in it.</para>
///
/// <para><b>Errors.</b> A unique-index violation raises <see cref="DuplicateEntryException"/>, as a
/// save does; any other database error raises <see cref="StoredProcedureException"/> naming the
/// procedure (never its parameter values).</para>
///
/// Scoped per request. Services use it; controllers may not (enforced by ArchitectureTests).
/// </summary>
public interface IStoredProcedureExecutor
{
    /// <summary>For procedures returning no rows. Answers with the rows affected (-1 under SET NOCOUNT ON).</summary>
    Task<int> ExecuteAsync(INonQueryProcedure procedure, CancellationToken cancellationToken = default);

    /// <summary>Every row of the first result set.</summary>
    Task<List<TRow>> QueryAsync<TRow>(IQueryProcedure<TRow> procedure, CancellationToken cancellationToken = default);

    /// <summary>The first row of the first result set, or null/default when there is none.</summary>
    Task<TRow?> QuerySingleAsync<TRow>(IQueryProcedure<TRow> procedure, CancellationToken cancellationToken = default);

    /// <summary>
    /// The first column of the first row. A non-nullable <typeparamref name="TValue"/> (bool, int)
    /// with no value to give throws rather than quietly answering false or 0; declare
    /// <c>IScalarProcedure&lt;bool?&gt;</c> when "no value" is a legitimate answer.
    /// </summary>
    Task<TValue> ScalarAsync<TValue>(IScalarProcedure<TValue> procedure, CancellationToken cancellationToken = default);
}

/// <summary>A stored procedure failed. Carries the procedure's name; the database's error is the inner exception.</summary>
public sealed class StoredProcedureException(string procedure, Exception inner)
    : Exception($"Stored procedure {procedure} failed: {inner.Message}", inner)
{
    public string Procedure { get; } = procedure;
}
