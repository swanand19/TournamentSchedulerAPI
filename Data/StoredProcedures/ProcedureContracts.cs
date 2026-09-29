namespace TournamentScheduler.Api.Data.StoredProcedures;

// What a procedure class implements says what the procedure returns. The executor method is picked
// by it, and the result type is inferred from it — so a call site never repeats the type, and asking
// for the wrong one is a compile error rather than a runtime surprise.

/// <summary>Base of every procedure class. Not implemented directly; pick one of the three below.</summary>
public interface IStoredProcedure { }

/// <summary>Returns no rows (an insert, update, delete, or anything answering only through outputs).</summary>
public interface INonQueryProcedure : IStoredProcedure { }

/// <summary>
/// Returns rows. <typeparamref name="TRow"/> is a class whose properties match the columns, or a
/// simple type (int, string, …) for a single-column result.
/// </summary>
public interface IQueryProcedure<TRow> : IStoredProcedure { }

/// <summary>Returns one value: the first column of the first row (a bool, a count, a name).</summary>
public interface IScalarProcedure<TValue> : IStoredProcedure { }
