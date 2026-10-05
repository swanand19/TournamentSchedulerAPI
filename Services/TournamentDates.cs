using TournamentScheduler.Api.Models;

namespace TournamentScheduler.Api.Services;

/// <summary>
/// A tournament's days, in its own time zone. "Today" and "midnight" mean the tournament's, not
/// the server's: a tournament in India ends at midnight in India wherever the server runs.
/// </summary>
public static class TournamentDates
{
    /// <summary>The grace day after the end date before a tournament completes (or cancels) by itself.</summary>
    public const int GraceDays = 1;

    public const int MaxLengthDays = 366;

    public static TimeZoneInfo ZoneOf(string? timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZoneId) ? Tournament.DefaultTimeZoneId : timeZoneId);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // A machine without that zone: India's offset, which has no daylight saving.
            return TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "India", "India");
        }
    }

    public static DateOnly Today(string? timeZoneId, DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), ZoneOf(timeZoneId)));

    /// <summary>The moment (UTC) it completes by itself: midnight at the end of the grace day after <paramref name="endDate"/>.</summary>
    public static DateTime ClosesAtUtc(DateOnly endDate, string? timeZoneId)
    {
        var localMidnight = endDate.AddDays(GraceDays + 1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, ZoneOf(timeZoneId));
    }

    /// <summary>The rule a new or edited tournament's dates must meet, or null when they do.</summary>
    public static string? Problem(DateOnly? start, DateOnly? end, string? timeZoneId, DateTime utcNow)
    {
        if (start == null || end == null) return "Choose the tournament's start and end dates.";
        if (end < start) return "The end date can't be before the start date.";
        if (end.Value.DayNumber - start.Value.DayNumber >= MaxLengthDays) return "A tournament can last at most a year.";
        if (end < Today(timeZoneId, utcNow)) return "The end date has already passed.";
        return null;
    }
}
