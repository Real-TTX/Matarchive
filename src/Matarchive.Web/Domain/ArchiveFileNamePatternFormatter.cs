using System.Globalization;

namespace Matarchive.Web.Domain;

public static class ArchiveFileNamePatternFormatter
{
    private static readonly (string Token, Func<DateTimeOffset, string> Value)[] DateTokens =
    [
        ("YYYY", timestamp => timestamp.Year.ToString("D4", CultureInfo.InvariantCulture)),
        ("yyyy", timestamp => timestamp.Year.ToString("D4", CultureInfo.InvariantCulture)),
        ("YY", timestamp => (timestamp.Year % 100).ToString("D2", CultureInfo.InvariantCulture)),
        ("yy", timestamp => (timestamp.Year % 100).ToString("D2", CultureInfo.InvariantCulture)),
        ("MM", timestamp => timestamp.Month.ToString("D2", CultureInfo.InvariantCulture)),
        ("dd", timestamp => timestamp.Day.ToString("D2", CultureInfo.InvariantCulture)),
        ("HH", timestamp => timestamp.Hour.ToString("D2", CultureInfo.InvariantCulture)),
        ("hh", timestamp =>
        {
            var hour = timestamp.Hour % 12;
            if (hour == 0)
            {
                hour = 12;
            }

            return hour.ToString("D2", CultureInfo.InvariantCulture);
        }),
        ("mm", timestamp => timestamp.Minute.ToString("D2", CultureInfo.InvariantCulture)),
        ("ss", timestamp => timestamp.Second.ToString("D2", CultureInfo.InvariantCulture)),
        ("fff", timestamp => timestamp.Millisecond.ToString("D3", CultureInfo.InvariantCulture))
    ];

    public static string Normalize(string? pattern, string? fallback = null)
    {
        var value = (pattern ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = (fallback ?? MatarchiveConstants.DefaultArchiveFileNamePattern).Trim();
        return string.IsNullOrWhiteSpace(value) ? MatarchiveConstants.DefaultArchiveFileNamePattern : value;
    }

    public static string BuildArchiveFileName(
        string? pattern,
        TaskDefinition task,
        ConnectionProfile source,
        ConnectionProfile destination,
        DateTimeOffset timestamp)
    {
        var normalizedPattern = Normalize(pattern);
        var expandedPattern = ExpandDateTokens(normalizedPattern, timestamp);

        expandedPattern = expandedPattern
            .Replace("{TaskName}", SanitizeSegment(task.Name), StringComparison.Ordinal)
            .Replace("{SourceName}", SanitizeSegment(source.Name), StringComparison.Ordinal)
            .Replace("{DestinationName}", SanitizeSegment(destination.Name), StringComparison.Ordinal)
            .Replace("{TaskType}", SanitizeSegment(task.TaskType), StringComparison.Ordinal);

        expandedPattern = expandedPattern
            .Replace("{", string.Empty, StringComparison.Ordinal)
            .Replace("}", string.Empty, StringComparison.Ordinal);

        var sanitized = SanitizeFileName(expandedPattern);
        if (sanitized.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return sanitized;
        }

        return $"{sanitized}.zip";
    }

    public static string GetHelpText()
    {
        return "Beispiele: Matarchive_{TaskName}_yyyyMMdd_HHmmss oder Backup_ddYYYYmmMM. Platzhalter: {TaskName}, {SourceName}, {DestinationName}, {TaskType}.";
    }

    private static string ExpandDateTokens(string pattern, DateTimeOffset timestamp)
    {
        var expanded = pattern;
        foreach (var (token, valueFactory) in DateTokens)
        {
            expanded = expanded.Replace(token, valueFactory(timestamp), StringComparison.Ordinal);
        }

        return expanded;
    }

    private static string SanitizeSegment(string value)
    {
        return SanitizeFileName(value.Trim());
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "matarchive" : sanitized;
    }
}
