using System.Collections.Concurrent;
using System.Data.Common;
using System.Reflection;

namespace TournamentScheduler.Api.Data.StoredProcedures;

/// <summary>
/// Turns result rows into objects. Columns match properties by name, ignoring case, or by
/// [ColumnName]; extra columns are ignored, and properties with no column keep their default. A
/// result where no column matches any property is refused — that is a wrong row type, not data.
///
/// A row type is inspected once; after that each row costs one compiled setter call per column.
/// </summary>
public static class RowMapper
{
    private sealed record Target(Action<object, object?> Set, Type Type, string Name);

    private static readonly ConcurrentDictionary<Type, Dictionary<string, Target>> Targets = new();

    /// <summary>A reader for the current result set, bound to its columns once.</summary>
    public static Func<DbDataReader, TRow> For<TRow>(DbDataReader reader)
    {
        var rowType = typeof(TRow);

        if (ValueConverter.IsSimple(rowType))
        {
            return r => (TRow)ValueConverter.ToProperty(r.IsDBNull(0) ? null : r.GetValue(0), rowType,
                () => $"the first column of a {rowType.Name} result")!;
        }

        if (rowType.GetConstructor(Type.EmptyTypes) == null)
            throw new InvalidOperationException($"{rowType.Name} needs a public parameterless constructor to be filled from result rows.");

        var targets = Targets.GetOrAdd(rowType, BuildTargets);
        var bindings = new List<(int Ordinal, Target Target)>();
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (targets.TryGetValue(reader.GetName(i), out var target) && bindings.All(b => b.Target != target))
                bindings.Add((i, target));
        }

        if (bindings.Count == 0 && reader.FieldCount > 0)
        {
            var columns = string.Join(", ", Enumerable.Range(0, reader.FieldCount).Select(reader.GetName));
            throw new InvalidOperationException($"None of the result's columns ({columns}) match a property of {rowType.Name}.");
        }

        var bound = bindings.ToArray();
        return r =>
        {
            var row = Activator.CreateInstance<TRow>()!;
            foreach (var (ordinal, target) in bound)
            {
                var value = r.IsDBNull(ordinal) ? null : r.GetValue(ordinal);
                target.Set(row, ValueConverter.ToProperty(value, target.Type, () => $"{rowType.Name}.{target.Name}"));
            }
            return row;
        };
    }

    private static Dictionary<string, Target> BuildTargets(Type rowType)
    {
        var targets = new Dictionary<string, Target>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in rowType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is not { IsPublic: true } || property.GetIndexParameters().Length > 0) continue;

            var column = property.GetCustomAttribute<ColumnNameAttribute>()?.Name ?? property.Name;
            targets.TryAdd(column, new Target(ProcedureParameter.CompileSetter(property), property.PropertyType, property.Name));
        }
        return targets;
    }
}
