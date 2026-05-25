using Microsoft.AspNetCore.Mvc.Rendering;

namespace Matarchive.Web.Domain;

public static class TaskRetentionPolicy
{
    public static IReadOnlyList<SelectListItem> RetentionModeOptions()
    {
        return
        [
            new("Keine automatische Retention", "None"),
            new("Letzte N Archive behalten", "KeepLast"),
            new("Archive nach X Tagen loeschen", "KeepDays"),
            new("Letzte N behalten und alte Archive loeschen", "KeepLastAndDays")
        ];
    }

    public static string NormalizeRetentionMode(string? value)
    {
        var candidate = (value ?? string.Empty).Trim();
        return MatarchiveConstants.RetentionModes.Contains(candidate, StringComparer.OrdinalIgnoreCase)
            ? MatarchiveConstants.RetentionModes.First(mode => string.Equals(mode, candidate, StringComparison.OrdinalIgnoreCase))
            : "None";
    }

    public static int? NormalizePositive(int? value)
    {
        return value.GetValueOrDefault() > 0 ? value : null;
    }

    public static string FormatSummary(TaskDefinition task)
    {
        return NormalizeRetentionMode(task.RetentionMode) switch
        {
            "KeepLast" => $"Letzte {task.RetentionKeepLast ?? 10} Archive behalten",
            "KeepDays" => $"Aelter als {task.RetentionKeepDays ?? 30} Tage loeschen",
            "KeepLastAndDays" => $"Letzte {task.RetentionKeepLast ?? 10} behalten, danach aelter als {task.RetentionKeepDays ?? 30} Tage loeschen",
            _ => "keine Retention"
        };
    }
}
