using System.Data;

namespace TournamentScheduler.Api.Data.StoredProcedures;

/// <summary>
/// Names the stored procedure a class calls, so no call site ever types the name. The name is a
/// compile-time constant and is validated before use: it can only ever be a procedure name, never
/// a piece of SQL.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class StoredProcedureAttribute : Attribute
{
    public StoredProcedureAttribute(string name)
    {
        Name = name;
    }

    /// <summary>"dbo.usp_GetStandings", "[reports].[usp_Leaders]" or just "usp_GetStandings".</summary>
    public string Name { get; }

    /// <summary>Seconds before the call is abandoned. 0 keeps the connection's default (30s).</summary>
    public int TimeoutSeconds { get; set; }
}

/// <summary>
/// Marks a property as one of the procedure's parameters. Unmarked properties are never sent, so a
/// helper property on the class can't leak into the call.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class QueryParamAttribute : Attribute
{
    public QueryParamAttribute() { }

    public QueryParamAttribute(ParameterDirection direction)
    {
        Direction = direction;
    }

    /// <summary>The SQL parameter name without "@". Defaults to the property name.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Input (default), Output, InputOutput, or ReturnValue (the procedure's RETURN code; the
    /// property must be an int). Outputs are written back onto the object after the call.
    /// </summary>
    public ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    /// <summary>
    /// Length for strings and byte arrays; -1 means MAX. Required for string and binary outputs,
    /// which SQL Server would otherwise truncate. Optional for inputs.
    /// </summary>
    public int Size { get; set; }

    /// <summary>Total digits for a decimal. Required, with <see cref="Scale"/>, for decimal outputs.</summary>
    public byte Precision { get; set; }

    /// <summary>Digits after the point for a decimal.</summary>
    public byte Scale { get; set; }

    /// <summary>The user-defined table type a <see cref="DataTable"/> property is sent as, e.g. "dbo.IntList".</summary>
    public string? TableType { get; set; }
}

/// <summary>Maps a result column to a property whose name differs from the column's.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public sealed class ColumnNameAttribute : Attribute
{
    public ColumnNameAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }
}
