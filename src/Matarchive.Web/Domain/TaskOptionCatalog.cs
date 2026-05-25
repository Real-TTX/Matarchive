using Microsoft.AspNetCore.Mvc.Rendering;

namespace Matarchive.Web.Domain;

public static class TaskOptionCatalog
{
    public static IReadOnlyList<SelectListItem> ArchiveFormatOptions()
    {
        return
        [
            new("ZIP-Archiv", "Zip"),
            new("Keine Archivdatei / Dateien direkt kopieren", "None")
        ];
    }

    public static IReadOnlyList<SelectListItem> CompressionLevelOptions()
    {
        return
        [
            new("Ausgewogen (typisch)", "Optimal"),
            new("Schnell", "Fastest"),
            new("Maximale Kompression", "SmallestSize"),
            new("Keine Kompression", "NoCompression")
        ];
    }

    public static IReadOnlyList<SelectListItem> TransferModeOptions()
    {
        return
        [
            new("Lokal stagen und verifizieren", "StagedLocal"),
            new("Direkt streamen, wenn der Connector es kann", "DirectStream")
        ];
    }

    public static string NormalizeArchiveFormat(string? value, bool legacyCompressToZip)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "ZIP" => "Zip",
            "NONE" => "None",
            "" => legacyCompressToZip ? "Zip" : "None",
            _ => legacyCompressToZip ? "Zip" : "None"
        };
    }

    public static string NormalizeCompressionLevel(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "FASTEST" => "Fastest",
            "SMALLESTSIZE" => "SmallestSize",
            "NOCOMPRESSION" => "NoCompression",
            "OPTIMAL" or "" => "Optimal",
            _ => "Optimal"
        };
    }

    public static string NormalizeTransferMode(string? value)
    {
        var normalized = Normalize(value);
        return normalized switch
        {
            "DIRECTSTREAM" => "DirectStream",
            "STAGEDLOCAL" or "" => "StagedLocal",
            _ => "StagedLocal"
        };
    }

    public static bool IsArchiveFormat(string? value)
    {
        return MatarchiveConstants.ArchiveFormats.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsCompressionLevel(string? value)
    {
        return MatarchiveConstants.CompressionLevels.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsTransferMode(string? value)
    {
        return MatarchiveConstants.TransferModes.Contains(value, StringComparer.OrdinalIgnoreCase);
    }

    private static string Normalize(string? value)
    {
        return (value ?? string.Empty).Trim().Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }
}
