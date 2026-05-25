using System.Globalization;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Matarchive.Web.Domain;

public static class TaskSchedulePolicy
{
    private static readonly IReadOnlyDictionary<string, DayOfWeek> DayNames = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
    {
        ["Monday"] = DayOfWeek.Monday,
        ["Tuesday"] = DayOfWeek.Tuesday,
        ["Wednesday"] = DayOfWeek.Wednesday,
        ["Thursday"] = DayOfWeek.Thursday,
        ["Friday"] = DayOfWeek.Friday,
        ["Saturday"] = DayOfWeek.Saturday,
        ["Sunday"] = DayOfWeek.Sunday
    };

    public static IReadOnlyList<SelectListItem> ScheduleModeOptions()
    {
        return
        [
            new("Manuell", "Manual"),
            new("Intervall", "Interval"),
            new("Taeglich", "Daily"),
            new("Woechentlich", "Weekly")
        ];
    }

    public static IReadOnlyList<SelectListItem> WeekdayOptions()
    {
        return
        [
            new("Montag", "Monday"),
            new("Dienstag", "Tuesday"),
            new("Mittwoch", "Wednesday"),
            new("Donnerstag", "Thursday"),
            new("Freitag", "Friday"),
            new("Samstag", "Saturday"),
            new("Sonntag", "Sunday")
        ];
    }

    public static string NormalizeScheduleMode(string? value, int? legacyRunEveryMinutes)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return legacyRunEveryMinutes.GetValueOrDefault() > 0 ? "Interval" : "Manual";
        }

        return MatarchiveConstants.ScheduleModes.Contains(candidate, StringComparer.OrdinalIgnoreCase)
            ? MatarchiveConstants.ScheduleModes.First(mode => string.Equals(mode, candidate, StringComparison.OrdinalIgnoreCase))
            : "Manual";
    }

    public static string NormalizeScheduleTime(string? value)
    {
        return TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time.ToString("HH:mm", CultureInfo.InvariantCulture)
            : "02:00";
    }

    public static string NormalizeScheduleDays(IEnumerable<string?> values)
    {
        var days = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Where(value => DayNames.ContainsKey(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => DayNames[value])
            .OrderBy(day => ((int)day + 6) % 7)
            .Select(day => day.ToString());

        return string.Join(',', days);
    }

    public static IReadOnlySet<DayOfWeek> ParseScheduleDays(string? value)
    {
        var days = (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(day => DayNames.ContainsKey(day))
            .Select(day => DayNames[day])
            .ToHashSet();

        return days;
    }

    public static string FormatSummary(TaskDefinition task)
    {
        var mode = NormalizeScheduleMode(task.ScheduleMode, task.RunEveryMinutes);
        return mode switch
        {
            "Interval" => $"Alle {task.ScheduleIntervalMinutes ?? task.RunEveryMinutes ?? 0} Minuten",
            "Daily" => $"Taeglich um {NormalizeScheduleTime(task.ScheduleTime)}",
            "Weekly" => $"Woechentlich {FormatDays(task.ScheduleDays)} um {NormalizeScheduleTime(task.ScheduleTime)}",
            _ => "manuell"
        };
    }

    public static DateTimeOffset? GetDueOccurrence(TaskDefinition task, IReadOnlyList<TaskRun> runs, DateTimeOffset nowUtc)
    {
        var mode = NormalizeScheduleMode(task.ScheduleMode, task.RunEveryMinutes);
        return mode switch
        {
            "Interval" => GetIntervalDueOccurrence(task, runs, nowUtc),
            "Daily" => GetCalendarDueOccurrence(task, runs, nowUtc, weekly: false),
            "Weekly" => GetCalendarDueOccurrence(task, runs, nowUtc, weekly: true),
            _ => null
        };
    }

    private static DateTimeOffset? GetIntervalDueOccurrence(TaskDefinition task, IReadOnlyList<TaskRun> runs, DateTimeOffset nowUtc)
    {
        var minutes = task.ScheduleIntervalMinutes ?? task.RunEveryMinutes;
        if (minutes.GetValueOrDefault() <= 0)
        {
            return null;
        }

        var lastFinished = runs
            .Where(run => run.TaskId == task.Id && run.FinishedAt.HasValue)
            .OrderByDescending(run => run.FinishedAt)
            .FirstOrDefault();

        var baseline = lastFinished?.FinishedAt ?? task.LastRunAt ?? task.CreatedAt;
        var interval = TimeSpan.FromMinutes(minutes!.Value);
        return nowUtc - baseline >= interval ? nowUtc : null;
    }

    private static DateTimeOffset? GetCalendarDueOccurrence(TaskDefinition task, IReadOnlyList<TaskRun> runs, DateTimeOffset nowUtc, bool weekly)
    {
        var timeZone = ResolveTimeZone(task.ScheduleTimeZoneId);
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var scheduledTime = TimeOnly.ParseExact(NormalizeScheduleTime(task.ScheduleTime), "HH:mm", CultureInfo.InvariantCulture);
        var allowedDays = weekly ? ParseScheduleDays(task.ScheduleDays) : new HashSet<DayOfWeek>();

        for (var offset = 0; offset < 8; offset++)
        {
            var candidateDate = DateOnly.FromDateTime(nowLocal.DateTime).AddDays(-offset);
            if (weekly && !allowedDays.Contains(candidateDate.DayOfWeek))
            {
                continue;
            }

            var candidateLocal = candidateDate.ToDateTime(scheduledTime);
            if (candidateLocal > nowLocal.DateTime)
            {
                continue;
            }

            var occurrence = new DateTimeOffset(candidateLocal, timeZone.GetUtcOffset(candidateLocal)).ToUniversalTime();
            var alreadyQueued = runs.Any(run =>
                run.TaskId == task.Id &&
                string.Equals(run.Trigger, "Scheduler", StringComparison.OrdinalIgnoreCase) &&
                run.QueuedAt >= occurrence);

            return alreadyQueued ? null : occurrence;
        }

        return null;
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(timeZoneId) ? MatarchiveConstants.DefaultScheduleTimeZoneId : timeZoneId.Trim();
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (string.Equals(id, MatarchiveConstants.DefaultScheduleTimeZoneId, StringComparison.OrdinalIgnoreCase))
        {
            return TryFindWindowsCentralEuropeTimeZone();
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static TimeZoneInfo TryFindWindowsCentralEuropeTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static string FormatDays(string? value)
    {
        var days = ParseScheduleDays(value);
        if (days.Count == 0)
        {
            return "ohne Wochentag";
        }

        var labels = WeekdayOptions().ToDictionary(item => item.Value, item => item.Text, StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", days
            .OrderBy(day => ((int)day + 6) % 7)
            .Select(day => labels[day.ToString()]));
    }
}
