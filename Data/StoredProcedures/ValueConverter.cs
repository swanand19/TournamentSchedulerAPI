using System.Globalization;

namespace TournamentScheduler.Api.Data.StoredProcedures;

/// <summary>
/// Turns a value from the database into the type of the property receiving it. A NULL for a
/// property that can't hold null (int, bool, DateTime) is an error naming the property — rather
/// than a quiet 0 or false that looks like real data.
/// </summary>
internal static class ValueConverter
{
    public static object? ToProperty(object? value, Type target, Func<string> describe)
    {
        var underlying = Nullable.GetUnderlyingType(target);
        var canBeNull = underlying != null || !target.IsValueType;
        var type = underlying ?? target;

        if (value is null or DBNull)
        {
            if (canBeNull) return null;
            throw new InvalidOperationException(
                $"The database returned NULL for {describe()}, which can't hold null. Make it {target.Name}? if NULL is a real answer.");
        }

        if (type.IsInstanceOfType(value)) return value;

        try
        {
            if (type.IsEnum)
                return value is string name ? Enum.Parse(type, name, ignoreCase: true) : Enum.ToObject(type, value);
            if (type == typeof(Guid))
                return value is string g ? Guid.Parse(g) : new Guid((byte[])value);
            if (type == typeof(DateTimeOffset) && value is DateTime dt)
                return new DateTimeOffset(dt);
            if (type == typeof(DateOnly) && value is DateTime date)
                return DateOnly.FromDateTime(date);
            if (type == typeof(TimeOnly) && value is TimeSpan time)
                return TimeOnly.FromTimeSpan(time);

            return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidOperationException(
                $"The database returned a {value.GetType().Name} ({value}) for {describe()}, which can't be read as {type.Name}.", ex);
        }
    }

    /// <summary>Types read straight from a single column rather than mapped property by property.</summary>
    public static bool IsSimple(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime)
               || t == typeof(DateTimeOffset) || t == typeof(DateOnly) || t == typeof(TimeOnly) || t == typeof(TimeSpan)
               || t == typeof(Guid) || t == typeof(byte[]);
    }
}
