using Microsoft.AspNetCore.Mvc.Rendering;

namespace Matarchive.Web.Domain;

[Flags]
public enum ConnectionCapability
{
    None = 0,
    Read = 1,
    Write = 2
}

public sealed record ConnectionValidationIssue(string FieldName, string Message);

public sealed record ConnectionTypeDescriptor(
    string Type,
    string DisplayName,
    string Description,
    int DefaultPort,
    bool ShowPort,
    bool ShowSsl,
    bool ShowRemotePath,
    string HostLabel,
    string RemotePathLabel,
    string RemotePathPlaceholder,
    ConnectionCapability SupportedCapabilities,
    bool UsesMailProtocols);

public interface IConnectionDriverDefinition
{
    string Type { get; }
    string DisplayName { get; }
    string Description { get; }
    ConnectionCapability SupportedCapabilities { get; }
    ConnectionTypeDescriptor Descriptor { get; }
    ConnectionCapability GetEffectiveCapabilities(ConnectionProfile profile);
    void ApplyDefaults(ConnectionProfile profile, string? originalType = null, bool forceTypeDefaults = false);
    IReadOnlyList<ConnectionValidationIssue> Validate(ConnectionProfile profile, bool requireIncomingSecret, bool requireOutgoingSecret);
}

public interface IConnectionReaderDriver : IConnectionDriverDefinition
{
}

public interface IConnectionWriterDriver : IConnectionDriverDefinition
{
}

public abstract class ConnectionDriverBase : IConnectionDriverDefinition
{
    public abstract ConnectionTypeDescriptor Descriptor { get; }

    public string Type => Descriptor.Type;
    public string DisplayName => Descriptor.DisplayName;
    public string Description => Descriptor.Description;
    public ConnectionCapability SupportedCapabilities => Descriptor.SupportedCapabilities;

    public virtual ConnectionCapability GetEffectiveCapabilities(ConnectionProfile profile)
    {
        var capabilities = ConnectionCapability.None;
        if (profile.CanRead && SupportedCapabilities.HasFlag(ConnectionCapability.Read))
        {
            capabilities |= ConnectionCapability.Read;
        }

        if (profile.CanWrite && SupportedCapabilities.HasFlag(ConnectionCapability.Write))
        {
            capabilities |= ConnectionCapability.Write;
        }

        return capabilities;
    }

    public virtual void ApplyDefaults(ConnectionProfile profile, string? originalType = null, bool forceTypeDefaults = false)
    {
        profile.Type = Type;
        if (forceTypeDefaults || !profile.CanRead && !profile.CanWrite)
        {
            profile.CanRead = SupportedCapabilities.HasFlag(ConnectionCapability.Read);
            profile.CanWrite = SupportedCapabilities.HasFlag(ConnectionCapability.Write);
        }

        if (!SupportedCapabilities.HasFlag(ConnectionCapability.Read))
        {
            profile.CanRead = false;
        }

        if (!SupportedCapabilities.HasFlag(ConnectionCapability.Write))
        {
            profile.CanWrite = false;
        }

        if (!Descriptor.ShowPort)
        {
            profile.Port = Descriptor.DefaultPort;
        }
        else if (forceTypeDefaults || profile.Port <= 0)
        {
            profile.Port = Descriptor.DefaultPort;
        }

        if (!Descriptor.ShowSsl)
        {
            profile.UseSsl = false;
        }

        if (!Descriptor.ShowRemotePath)
        {
            profile.RemotePath = string.Empty;
        }

        profile.IncomingProtocol = string.Empty;
        profile.OutgoingProtocol = string.Empty;
        profile.OutgoingHost = string.Empty;
        profile.OutgoingPort = null;
        profile.OutgoingUseSsl = false;
        profile.OutgoingUsername = string.Empty;
        profile.OutgoingSecret = string.Empty;
    }

    public virtual IReadOnlyList<ConnectionValidationIssue> Validate(
        ConnectionProfile profile,
        bool requireIncomingSecret,
        bool requireOutgoingSecret)
    {
        var issues = new List<ConnectionValidationIssue>();
        ValidateCapabilities(profile, issues);

        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.Host), $"{Descriptor.HostLabel} ist erforderlich."));
        }

        if (Descriptor.ShowPort && !IsValidPort(profile.Port))
        {
            issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.Port), "Port muss zwischen 1 und 65535 liegen."));
        }

        if (string.IsNullOrWhiteSpace(profile.Username))
        {
            issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.Username), "Benutzername ist erforderlich."));
        }

        if (requireIncomingSecret && string.IsNullOrWhiteSpace(profile.Secret))
        {
            issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.Secret), "Passwort ist erforderlich."));
        }

        if (Descriptor.ShowRemotePath && string.IsNullOrWhiteSpace(profile.RemotePath))
        {
            issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.RemotePath), $"{Descriptor.RemotePathLabel} ist erforderlich."));
        }

        return issues;
    }

    protected static void ValidateCapabilities(ConnectionProfile profile, List<ConnectionValidationIssue> issues)
    {
        if (!profile.CanRead && !profile.CanWrite)
        {
            issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.CanRead), "Mindestens Lesen oder Schreiben muss aktiviert sein."));
        }
    }

    protected static bool IsValidPort(int? port)
    {
        return port is >= 1 and <= 65535;
    }
}

public sealed class EmailConnectionDriver : ConnectionDriverBase, IConnectionReaderDriver, IConnectionWriterDriver
{
    public override ConnectionTypeDescriptor Descriptor { get; } = new(
        "EMAIL",
        "E-Mail",
        "Ein Mailprofil kann per IMAP/POP3 lesen und per SMTP schreiben. Die Richtung wird pro Profil aktiviert.",
        993,
        true,
        true,
        true,
        "Posteingangsserver",
        "Mailbox / Ordner",
        "z. B. INBOX oder Archiv",
        ConnectionCapability.Read | ConnectionCapability.Write,
        UsesMailProtocols: true);

    public override void ApplyDefaults(ConnectionProfile profile, string? originalType = null, bool forceTypeDefaults = false)
    {
        var legacyProtocol = ConnectionTypeCatalog.GetLegacyMailProtocol(originalType);
        profile.Type = Type;

        if (forceTypeDefaults || !profile.CanRead && !profile.CanWrite)
        {
            profile.CanRead = true;
            profile.CanWrite = false;
        }

        profile.IncomingProtocol = ConnectionTypeCatalog.NormalizeIncomingProtocol(
            ConnectionTypeCatalog.IsLegacyMailProtocol(originalType) || string.IsNullOrWhiteSpace(profile.IncomingProtocol)
                ? legacyProtocol
                : profile.IncomingProtocol);

        if (forceTypeDefaults || profile.Port <= 0)
        {
            profile.Port = GetDefaultIncomingPort(profile.IncomingProtocol);
            profile.UseSsl = string.Equals(profile.IncomingProtocol, "IMAP", StringComparison.OrdinalIgnoreCase);
        }

        if (profile.CanRead &&
            string.Equals(profile.IncomingProtocol, "IMAP", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(profile.RemotePath))
        {
            profile.RemotePath = "INBOX";
        }

        profile.OutgoingProtocol = "SMTP";
        if (profile.OutgoingPort is null or <= 0)
        {
            profile.OutgoingPort = 587;
        }

        if (forceTypeDefaults)
        {
            profile.OutgoingUseSsl = true;
        }
    }

    public override IReadOnlyList<ConnectionValidationIssue> Validate(
        ConnectionProfile profile,
        bool requireIncomingSecret,
        bool requireOutgoingSecret)
    {
        var issues = new List<ConnectionValidationIssue>();
        ValidateCapabilities(profile, issues);

        if (!MatarchiveConstants.MailIncomingProtocols.Contains(profile.IncomingProtocol, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.IncomingProtocol), "Bitte IMAP oder POP3 waehlen."));
        }

        if (profile.CanRead)
        {
            if (string.IsNullOrWhiteSpace(profile.Host))
            {
                issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.Host), "Posteingangsserver ist erforderlich."));
            }

            if (!IsValidPort(profile.Port))
            {
                issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.Port), "Port muss zwischen 1 und 65535 liegen."));
            }

            if (string.IsNullOrWhiteSpace(profile.Username))
            {
                issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.Username), "Benutzername ist fuer Lesen erforderlich."));
            }

            if (requireIncomingSecret && string.IsNullOrWhiteSpace(profile.Secret))
            {
                issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.Secret), "Passwort ist fuer Lesen erforderlich."));
            }
        }

        if (profile.CanWrite)
        {
            if (string.IsNullOrWhiteSpace(profile.OutgoingHost))
            {
                issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.OutgoingHost), "SMTP-Server ist erforderlich."));
            }

            if (!IsValidPort(profile.OutgoingPort))
            {
                issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.OutgoingPort), "SMTP-Port muss zwischen 1 und 65535 liegen."));
            }

            if (string.IsNullOrWhiteSpace(profile.OutgoingUsername))
            {
                issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.OutgoingUsername), "SMTP-Benutzername ist erforderlich."));
            }

            if (requireOutgoingSecret && string.IsNullOrWhiteSpace(profile.OutgoingSecret))
            {
                issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.OutgoingSecret), "SMTP-Passwort ist erforderlich."));
            }
        }

        return issues;
    }

    private static int GetDefaultIncomingPort(string protocol)
    {
        return string.Equals(protocol, "POP3", StringComparison.OrdinalIgnoreCase) ? 110 : 993;
    }
}

public sealed class SmbConnectionDriver : ConnectionDriverBase, IConnectionReaderDriver, IConnectionWriterDriver
{
    public override ConnectionTypeDescriptor Descriptor { get; } = new(
        "SMB",
        "SMB",
        "Dateifreigabe als Quelle oder Ziel. Host und Freigabe/Pfad bleiben getrennt.",
        445,
        false,
        false,
        true,
        "Server",
        "Freigabe / Pfad",
        @"z. B. Backup\Test1",
        ConnectionCapability.Read | ConnectionCapability.Write,
        UsesMailProtocols: false);

    public override IReadOnlyList<ConnectionValidationIssue> Validate(
        ConnectionProfile profile,
        bool requireIncomingSecret,
        bool requireOutgoingSecret)
    {
        var issues = base.Validate(profile, requireIncomingSecret, requireOutgoingSecret).ToList();

        if (profile.Host.Contains('\\', StringComparison.Ordinal) || profile.Host.Contains('/', StringComparison.Ordinal))
        {
            issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.Host), "Beim SMB-Host nur Servername oder IP eintragen, keinen UNC-Pfad."));
        }

        var normalizedSmbPath = ConnectionTypeCatalog.NormalizeRemotePath(Type, profile.RemotePath);
        if (normalizedSmbPath.StartsWith(@"\\", StringComparison.Ordinal) || normalizedSmbPath.StartsWith("//", StringComparison.Ordinal))
        {
            issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.RemotePath), @"Beim SMB-Pfad nur Freigabe und Unterordner eintragen, z. B. Backup\Test2."));
        }

        var smbPath = normalizedSmbPath.Trim('\\');
        var segments = smbPath.Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            issues.Add(new ConnectionValidationIssue(nameof(ConnectionProfile.RemotePath), "Der SMB-Pfad muss mindestens die Freigabe enthalten."));
        }

        return issues;
    }
}

public sealed class CustomConnectionDriver : ConnectionDriverBase, IConnectionReaderDriver, IConnectionWriterDriver
{
    public override ConnectionTypeDescriptor Descriptor { get; } = new(
        "CUSTOM",
        "Benutzerdefiniert",
        "Freies Dateisystemprofil fuer lokale oder gemountete Pfade im Container.",
        0,
        true,
        true,
        true,
        "Zielhost",
        "Pfad / Kontext",
        "Freier Zielpfad",
        ConnectionCapability.Read | ConnectionCapability.Write,
        UsesMailProtocols: false);
}

public static class ConnectionTypeCatalog
{
    private static readonly IConnectionDriverDefinition[] OrderedDrivers =
    [
        new EmailConnectionDriver(),
        new SmbConnectionDriver(),
        new CustomConnectionDriver()
    ];

    private static readonly IReadOnlyDictionary<string, IConnectionDriverDefinition> Drivers =
        OrderedDrivers.ToDictionary(driver => driver.Type, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<SelectListItem> GetOptions()
    {
        return OrderedDrivers
            .Select(driver => new SelectListItem(driver.DisplayName, driver.Type))
            .ToList();
    }

    public static IReadOnlyList<SelectListItem> GetIncomingProtocolOptions()
    {
        return MatarchiveConstants.MailIncomingProtocols
            .Select(protocol => new SelectListItem(protocol, protocol))
            .ToList();
    }

    public static bool TryGetDescriptor(string? type, out ConnectionTypeDescriptor descriptor)
    {
        var normalized = Normalize(type);
        if (Drivers.TryGetValue(normalized, out var driver))
        {
            descriptor = driver.Descriptor;
            return true;
        }

        descriptor = Drivers["EMAIL"].Descriptor;
        return false;
    }

    public static ConnectionTypeDescriptor GetDescriptor(string? type)
    {
        return GetDriver(type).Descriptor;
    }

    public static IConnectionDriverDefinition GetDriver(string? type)
    {
        var normalized = Normalize(type);
        return Drivers.TryGetValue(normalized, out var driver) ? driver : Drivers["EMAIL"];
    }

    public static string Normalize(string? type)
    {
        var candidate = (type ?? string.Empty).Trim();
        if (candidate.Equals("POP3", StringComparison.OrdinalIgnoreCase) ||
            candidate.Equals("IMAP", StringComparison.OrdinalIgnoreCase) ||
            candidate.Equals("MAIL", StringComparison.OrdinalIgnoreCase) ||
            candidate.Equals("EMAIL", StringComparison.OrdinalIgnoreCase) ||
            candidate.Equals("E-MAIL", StringComparison.OrdinalIgnoreCase))
        {
            return "EMAIL";
        }

        return string.IsNullOrWhiteSpace(candidate) ? "EMAIL" : candidate.ToUpperInvariant();
    }

    public static bool ApplyDefaults(ConnectionProfile profile, string? originalType = null, bool forceTypeDefaults = false)
    {
        var before = Snapshot(profile);
        var driver = GetDriver(profile.Type);
        driver.ApplyDefaults(profile, originalType, forceTypeDefaults);
        profile.RemotePath = NormalizeRemotePath(profile.Type, profile.RemotePath);
        return before != Snapshot(profile);
    }

    public static IReadOnlyList<ConnectionValidationIssue> ValidateProfile(
        ConnectionProfile profile,
        bool requireIncomingSecret,
        bool requireOutgoingSecret)
    {
        return GetDriver(profile.Type).Validate(profile, requireIncomingSecret, requireOutgoingSecret);
    }

    public static string GetSummary(ConnectionProfile profile)
    {
        var descriptor = GetDescriptor(profile.Type);
        if (IsEmail(profile))
        {
            var readPart = profile.CanRead
                ? $"{NormalizeIncomingProtocol(profile.IncomingProtocol)} {FormatEndpoint(profile.Host, profile.Port)}"
                : string.Empty;
            var writePart = profile.CanWrite
                ? $"SMTP {FormatEndpoint(profile.OutgoingHost, profile.OutgoingPort)}"
                : string.Empty;
            var parts = new[] { readPart, writePart }.Where(part => !string.IsNullOrWhiteSpace(part));
            return $"{descriptor.DisplayName} ({GetCapabilitySummary(profile)}) - {string.Join(" / ", parts)}";
        }

        var host = string.IsNullOrWhiteSpace(profile.Host) ? $"ohne {descriptor.HostLabel}" : profile.Host;
        var port = descriptor.ShowPort && profile.Port > 0 ? $":{profile.Port}" : string.Empty;
        var remotePath = NormalizeRemotePath(profile.Type, profile.RemotePath);
        var path = string.IsNullOrWhiteSpace(remotePath)
            ? string.Empty
            : descriptor.ShowRemotePath
                ? $" - {remotePath}"
                : string.Empty;

        return $"{descriptor.DisplayName} ({GetCapabilitySummary(profile)}) - {host}{port}{path}";
    }

    public static string GetCapabilitySummary(ConnectionProfile profile)
    {
        var canRead = CanRead(profile);
        var canWrite = CanWrite(profile);
        return (canRead, canWrite) switch
        {
            (true, true) => "Lesen + Schreiben",
            (true, false) => "nur Lesen",
            (false, true) => "nur Schreiben",
            _ => "ohne Richtung"
        };
    }

    public static bool CanRead(ConnectionProfile? profile)
    {
        return profile is not null && GetDriver(profile.Type).GetEffectiveCapabilities(profile).HasFlag(ConnectionCapability.Read);
    }

    public static bool CanWrite(ConnectionProfile? profile)
    {
        return profile is not null && GetDriver(profile.Type).GetEffectiveCapabilities(profile).HasFlag(ConnectionCapability.Write);
    }

    public static bool IsSslVisible(string? type) => GetDescriptor(type).ShowSsl;

    public static bool IsRemotePathVisible(string? type) => GetDescriptor(type).ShowRemotePath;

    public static bool IsSmb(ConnectionProfile? profile)
    {
        return profile is not null && string.Equals(GetDescriptor(profile.Type).Type, "SMB", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsEmail(ConnectionProfile? profile)
    {
        return profile is not null && string.Equals(GetDescriptor(profile.Type).Type, "EMAIL", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeRemotePath(string? type, string? remotePath)
    {
        var value = (remotePath ?? string.Empty).Trim();
        if (string.Equals(GetDescriptor(type).Type, "SMB", StringComparison.OrdinalIgnoreCase))
        {
            value = value.Replace('/', '\\');
        }

        return value;
    }

    public static string NormalizeIncomingProtocol(string? protocol)
    {
        var candidate = (protocol ?? string.Empty).Trim();
        if (candidate.Equals("POP3", StringComparison.OrdinalIgnoreCase))
        {
            return "POP3";
        }

        return "IMAP";
    }

    public static string GetLegacyMailProtocol(string? type)
    {
        var candidate = (type ?? string.Empty).Trim();
        return candidate.Equals("POP3", StringComparison.OrdinalIgnoreCase) ? "POP3" : "IMAP";
    }

    public static bool IsLegacyMailProtocol(string? type)
    {
        var candidate = (type ?? string.Empty).Trim();
        return candidate.Equals("POP3", StringComparison.OrdinalIgnoreCase) ||
               candidate.Equals("IMAP", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatEndpoint(string host, int? port)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return "ohne Host";
        }

        return port.GetValueOrDefault() > 0 ? $"{host}:{port}" : host;
    }

    private static string Snapshot(ConnectionProfile profile)
    {
        return string.Join('|',
            profile.Type,
            profile.CanRead,
            profile.CanWrite,
            profile.IncomingProtocol,
            profile.Host,
            profile.Port,
            profile.UseSsl,
            profile.Username,
            profile.RemotePath,
            profile.OutgoingProtocol,
            profile.OutgoingHost,
            profile.OutgoingPort,
            profile.OutgoingUseSsl,
            profile.OutgoingUsername);
    }
}
