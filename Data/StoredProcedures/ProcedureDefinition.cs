using System.Collections.Concurrent;
using System.Data;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace TournamentScheduler.Api.Data.StoredProcedures;

/// <summary>
/// Everything the executor needs to know about one procedure class — its name, timeout and
/// parameters — read from the attributes once and cached for the life of the app. A class that is
/// declared wrongly (no name, an output string with no size, a table without its type) fails the
/// first time it is used, with a message saying exactly what to fix, instead of failing inside SQL
/// Server or, worse, silently truncating a value.
/// </summary>
public sealed partial class ProcedureDefinition
{
    private static readonly ConcurrentDictionary<Type, ProcedureDefinition> Cache = new();

    public string Name { get; }
    public int TimeoutSeconds { get; }
    public IReadOnlyList<ProcedureParameter> Parameters { get; }

    private ProcedureDefinition(string name, int timeoutSeconds, IReadOnlyList<ProcedureParameter> parameters)
    {
        Name = name;
        TimeoutSeconds = timeoutSeconds;
        Parameters = parameters;
    }

    public static ProcedureDefinition For(Type procedureType) => Cache.GetOrAdd(procedureType, Build);

    /// <summary>The parameters for one call, carrying this instance's values.</summary>
    public SqlParameter[] CreateParameters(object procedure) =>
        Parameters.Select(p => p.Create(procedure)).ToArray();

    /// <summary>Copies output, input-output and return values back onto the instance after the call.</summary>
    public void ReadOutputs(object procedure, IReadOnlyList<SqlParameter> parameters)
    {
        for (var i = 0; i < Parameters.Count; i++)
        {
            var definition = Parameters[i];
            if (definition.Direction == ParameterDirection.Input) continue;
            definition.Write(procedure, parameters[i].Value);
        }
    }

    // ------------------------------------------------------------------
    // Reading the class
    // ------------------------------------------------------------------

    /// <summary>
    /// "name", "schema.name" or "database.schema.name", each part plain or [bracketed]. Anything else
    /// — a space, a semicolon, a quote — is refused, so the name can never carry SQL.
    /// </summary>
    [GeneratedRegex(@"^(?:\[[^\[\]]+\]|[A-Za-z_][\w@#$]*)(?:\.(?:\[[^\[\]]+\]|[A-Za-z_][\w@#$]*)){0,2}$")]
    private static partial Regex ProcedureNamePattern();

    [GeneratedRegex(@"^[A-Za-z_@#][\w@#$]*$")]
    private static partial Regex ParameterNamePattern();

    private static ProcedureDefinition Build(Type type)
    {
        var attribute = type.GetCustomAttribute<StoredProcedureAttribute>()
            ?? throw Invalid(type, $"is missing [{nameof(StoredProcedureAttribute).Replace("Attribute", "")}(\"schema.name\")].");

        if (string.IsNullOrWhiteSpace(attribute.Name) || !ProcedureNamePattern().IsMatch(attribute.Name))
            throw Invalid(type, $"names the procedure \"{attribute.Name}\", which is not a valid procedure name.");

        if (attribute.TimeoutSeconds < 0)
            throw Invalid(type, "has a negative timeout.");

        var parameters = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (Property: p, Attribute: p.GetCustomAttribute<QueryParamAttribute>()))
            .Where(x => x.Attribute != null)
            .OrderBy(x => x.Property.MetadataToken) // declaration order, so the list reads like the class
            .Select(x => ProcedureParameter.Build(type, x.Property, x.Attribute!))
            .ToList();

        var duplicate = parameters.GroupBy(p => p.ParameterName, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicate != null)
            throw Invalid(type, $"sends {duplicate.Key} more than once.");

        if (parameters.Count(p => p.Direction == ParameterDirection.ReturnValue) > 1)
            throw Invalid(type, "has more than one ReturnValue parameter.");

        return new ProcedureDefinition(attribute.Name, attribute.TimeoutSeconds, parameters);
    }

    internal static InvalidOperationException Invalid(Type type, string problem) =>
        new($"Stored procedure class {type.Name} {problem}");

    internal static bool IsValidParameterName(string name) => ParameterNamePattern().IsMatch(name);
}

/// <summary>One [QueryParam] property: how it is sent, and how an output comes back.</summary>
public sealed class ProcedureParameter
{
    private const int MaxNVarChar = 4000;
    private const int MaxVarBinary = 8000;

    private readonly Func<object, object?> _get;
    private readonly Action<object, object?>? _set;

    public PropertyInfo Property { get; }

    /// <summary>With the leading "@".</summary>
    public string ParameterName { get; }

    public ParameterDirection Direction { get; }
    public SqlDbType SqlType { get; }
    public int Size { get; }
    public byte Precision { get; }
    public byte Scale { get; }
    public string? TableType { get; }

    private ProcedureParameter(
        PropertyInfo property, string parameterName, ParameterDirection direction, SqlDbType sqlType,
        int size, byte precision, byte scale, string? tableType)
    {
        Property = property;
        ParameterName = parameterName;
        Direction = direction;
        SqlType = sqlType;
        Size = size;
        Precision = precision;
        Scale = scale;
        TableType = tableType;
        _get = CompileGetter(property);
        _set = direction == ParameterDirection.Input ? null : CompileSetter(property);
    }

    internal static ProcedureParameter Build(Type owner, PropertyInfo property, QueryParamAttribute attribute)
    {
        string Where() => $"property {property.Name}";

        var name = attribute.Name ?? property.Name;
        if (name.StartsWith('@')) name = name[1..];
        if (!ProcedureDefinition.IsValidParameterName(name))
            throw ProcedureDefinition.Invalid(owner, $"gives {Where()} the parameter name \"{name}\", which is not valid.");

        if (property.GetMethod is not { IsPublic: true })
            throw ProcedureDefinition.Invalid(owner, $"needs a public getter on {Where()}.");

        var direction = attribute.Direction;
        if (direction != ParameterDirection.Input && property.SetMethod is not { IsPublic: true })
            throw ProcedureDefinition.Invalid(owner, $"needs a public setter on {Where()} to receive its {direction} value.");

        var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        var sqlType = SqlTypeFor(valueType)
            ?? throw ProcedureDefinition.Invalid(owner,
                $"uses {property.PropertyType.Name} for {Where()}. Supported: string, int, long, short, byte, bool, decimal, " +
                "double, float, DateTime, DateTimeOffset, DateOnly, TimeOnly, TimeSpan, Guid, byte[], enums, and DataTable (table types).");

        if (direction == ParameterDirection.ReturnValue && valueType != typeof(int))
            throw ProcedureDefinition.Invalid(owner, $"declares {Where()} as the ReturnValue; a procedure's RETURN code is an int.");

        var isTable = sqlType == SqlDbType.Structured;
        if (isTable && string.IsNullOrWhiteSpace(attribute.TableType))
            throw ProcedureDefinition.Invalid(owner, $"sends {Where()} as a table; set TableType to its SQL type, e.g. [QueryParam(TableType = \"dbo.IntList\")].");
        if (!isTable && attribute.TableType != null)
            throw ProcedureDefinition.Invalid(owner, $"sets TableType on {Where()}, which is not a DataTable.");
        if (isTable && direction != ParameterDirection.Input)
            throw ProcedureDefinition.Invalid(owner, $"declares table {Where()} as {direction}; table-valued parameters are input only.");

        var receives = direction is ParameterDirection.Output or ParameterDirection.InputOutput;
        if (receives && sqlType is SqlDbType.NVarChar or SqlDbType.VarBinary && attribute.Size == 0)
            throw ProcedureDefinition.Invalid(owner, $"needs a Size on {direction} {Where()} (-1 for MAX), or SQL Server truncates it.");
        if (receives && sqlType == SqlDbType.Decimal && attribute.Precision == 0)
            throw ProcedureDefinition.Invalid(owner, $"needs Precision and Scale on {direction} decimal {Where()}, or SQL Server rounds it.");
        if (attribute.Size < -1)
            throw ProcedureDefinition.Invalid(owner, $"gives {Where()} a Size below -1.");

        return new ProcedureParameter(property, "@" + name, direction, sqlType,
            attribute.Size, attribute.Precision, attribute.Scale, attribute.TableType);
    }

    internal static SqlDbType? SqlTypeFor(Type t)
    {
        if (t.IsEnum) t = Enum.GetUnderlyingType(t);

        if (t == typeof(string)) return SqlDbType.NVarChar;
        if (t == typeof(int)) return SqlDbType.Int;
        if (t == typeof(long)) return SqlDbType.BigInt;
        if (t == typeof(short)) return SqlDbType.SmallInt;
        if (t == typeof(byte)) return SqlDbType.TinyInt;
        if (t == typeof(bool)) return SqlDbType.Bit;
        if (t == typeof(decimal)) return SqlDbType.Decimal;
        if (t == typeof(double)) return SqlDbType.Float;
        if (t == typeof(float)) return SqlDbType.Real;
        if (t == typeof(DateTime)) return SqlDbType.DateTime2;
        if (t == typeof(DateTimeOffset)) return SqlDbType.DateTimeOffset;
        if (t == typeof(DateOnly)) return SqlDbType.Date;
        if (t == typeof(TimeOnly) || t == typeof(TimeSpan)) return SqlDbType.Time;
        if (t == typeof(Guid)) return SqlDbType.UniqueIdentifier;
        if (t == typeof(byte[])) return SqlDbType.VarBinary;
        if (t == typeof(DataTable)) return SqlDbType.Structured;
        return null;
    }

    /// <summary>A fresh SqlParameter holding this instance's value.</summary>
    public SqlParameter Create(object procedure)
    {
        var parameter = new SqlParameter(ParameterName, SqlType) { Direction = Direction };

        if (Direction != ParameterDirection.ReturnValue)
        {
            var value = _get(procedure);
            if (value != null && value.GetType().IsEnum)
                value = Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()));
            if (value is TimeOnly time)
                value = time.ToTimeSpan();
            parameter.Value = value ?? DBNull.Value;
            parameter.Size = SizeFor(value);
        }

        if (SqlType == SqlDbType.Decimal && Precision > 0)
        {
            parameter.Precision = Precision;
            parameter.Scale = Scale;
        }

        if (TableType != null)
            parameter.TypeName = TableType;

        return parameter;
    }

    /// <summary>
    /// An input string is sent as nvarchar(4000) — or MAX when longer — rather than its exact length,
    /// so every call shares one parameter shape and SQL Server reuses one cached plan instead of
    /// compiling a new plan per distinct length.
    /// </summary>
    private int SizeFor(object? value)
    {
        if (Size != 0) return Size;
        return value switch
        {
            string s => s.Length <= MaxNVarChar ? MaxNVarChar : -1,
            byte[] b => b.Length <= MaxVarBinary ? MaxVarBinary : -1,
            _ when SqlType == SqlDbType.NVarChar => MaxNVarChar,
            _ when SqlType == SqlDbType.VarBinary => MaxVarBinary,
            _ => 0
        };
    }

    /// <summary>Puts a value the database sent back onto the property.</summary>
    public void Write(object procedure, object? databaseValue)
    {
        var value = ValueConverter.ToProperty(databaseValue, Property.PropertyType,
            () => $"{procedure.GetType().Name}.{Property.Name} ({ParameterName})");
        _set!(procedure, value);
    }

    // Compiled once per property: a call reads and writes values at the speed of normal code, not reflection.

    private static Func<object, object?> CompileGetter(PropertyInfo property)
    {
        var instance = Expression.Parameter(typeof(object));
        var body = Expression.Convert(Expression.Property(Expression.Convert(instance, property.DeclaringType!), property), typeof(object));
        return Expression.Lambda<Func<object, object?>>(body, instance).Compile();
    }

    internal static Action<object, object?> CompileSetter(PropertyInfo property)
    {
        var instance = Expression.Parameter(typeof(object));
        var value = Expression.Parameter(typeof(object));
        var body = Expression.Assign(
            Expression.Property(Expression.Convert(instance, property.DeclaringType!), property),
            Expression.Convert(value, property.PropertyType));
        return Expression.Lambda<Action<object, object?>>(body, instance, value).Compile();
    }
}
