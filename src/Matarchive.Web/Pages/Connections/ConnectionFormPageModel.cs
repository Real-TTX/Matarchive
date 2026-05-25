using System.ComponentModel.DataAnnotations;
using Matarchive.Web.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Matarchive.Web.Pages.Connections;

public abstract class ConnectionFormPageModel : AppPageModel
{
    [BindProperty]
    [Display(Name = "Name")]
    public string Name { get; set; } = "";

    [BindProperty]
    [Display(Name = "Typ")]
    public string Type { get; set; } = "EMAIL";

    [BindProperty]
    [Display(Name = "Lesen erlauben")]
    public bool CanRead { get; set; } = true;

    [BindProperty]
    [Display(Name = "Schreiben erlauben")]
    public bool CanWrite { get; set; }

    [BindProperty]
    [Display(Name = "Eingangsprotokoll")]
    public string IncomingProtocol { get; set; } = "IMAP";

    [BindProperty]
    [Display(Name = "Host")]
    public string Host { get; set; } = "";

    [BindProperty]
    [Display(Name = "Port")]
    public int Port { get; set; }

    [BindProperty]
    public string OriginalType { get; set; } = "EMAIL";

    [BindProperty]
    public int OriginalPort { get; set; }

    [BindProperty]
    [Display(Name = "SSL/TLS")]
    public bool UseSsl { get; set; }

    [BindProperty]
    [Display(Name = "Benutzername")]
    public string Username { get; set; } = "";

    [BindProperty]
    [DataType(DataType.Password)]
    [Display(Name = "Passwort")]
    public string Secret { get; set; } = "";

    [BindProperty]
    [Display(Name = "SMTP-Server")]
    public string OutgoingHost { get; set; } = "";

    [BindProperty]
    [Display(Name = "SMTP-Port")]
    public int? OutgoingPort { get; set; } = 587;

    [BindProperty]
    [Display(Name = "SMTP SSL/TLS")]
    public bool OutgoingUseSsl { get; set; } = true;

    [BindProperty]
    [Display(Name = "SMTP-Benutzername")]
    public string OutgoingUsername { get; set; } = "";

    [BindProperty]
    [DataType(DataType.Password)]
    [Display(Name = "SMTP-Passwort")]
    public string OutgoingSecret { get; set; } = "";

    [BindProperty]
    [Display(Name = "Freigabe / Pfad")]
    public string RemotePath { get; set; } = "";

    [BindProperty]
    [Display(Name = "Notiz")]
    public string Notes { get; set; } = "";

    public IReadOnlyList<SelectListItem> TypeOptions { get; private set; } = [];
    public IReadOnlyList<SelectListItem> IncomingProtocolOptions { get; private set; } = [];

    public ConnectionTypeDescriptor SelectedType => ConnectionTypeCatalog.GetDescriptor(Type);
    public bool ShowPort => SelectedType.ShowPort;
    public bool ShowSsl => SelectedType.ShowSsl;
    public bool ShowRemotePath => SelectedType.ShowRemotePath;
    public bool IsEmail => string.Equals(SelectedType.Type, "EMAIL", StringComparison.OrdinalIgnoreCase);
    public bool ShowCommonEndpoint => !IsEmail;
    public bool ShowEmailReadEndpoint => IsEmail && CanRead;
    public bool ShowEmailWriteEndpoint => IsEmail && CanWrite;
    public string HostLabel => SelectedType.HostLabel;
    public string RemotePathLabel => SelectedType.RemotePathLabel;
    public string RemotePathPlaceholder => SelectedType.RemotePathPlaceholder;
    public string TypeDescription => SelectedType.Description;
    public string ConnectionHeading => $"Profil fuer {SelectedType.DisplayName}";
    public string CapabilitySummary => ConnectionTypeCatalog.GetCapabilitySummary(ToProfile());

    protected void PrepareTypeOptions()
    {
        TypeOptions = ConnectionTypeCatalog.GetOptions();
        IncomingProtocolOptions = ConnectionTypeCatalog.GetIncomingProtocolOptions();
    }

    protected void ApplyTypeDefaults()
    {
        var normalizedType = ConnectionTypeCatalog.Normalize(Type);
        var typeChanged = !string.Equals(normalizedType, ConnectionTypeCatalog.Normalize(OriginalType), StringComparison.OrdinalIgnoreCase);
        var profile = ToProfile();
        profile.Type = normalizedType;

        if (typeChanged && Port == OriginalPort)
        {
            profile.Port = 0;
        }

        ConnectionTypeCatalog.ApplyDefaults(profile, OriginalType);
        LoadFromProfile(profile, keepSecrets: true);
    }

    protected bool ValidateConnectionInput(bool requireIncomingSecret, bool requireOutgoingSecret)
    {
        var valid = true;

        if (string.IsNullOrWhiteSpace(Name))
        {
            ModelState.AddModelError(nameof(Name), "Name ist erforderlich.");
            valid = false;
        }

        if (!ConnectionTypeCatalog.TryGetDescriptor(Type, out _))
        {
            ModelState.AddModelError(nameof(Type), "Bitte einen gueltigen Typ waehlen.");
            valid = false;
        }

        var profile = ToProfile();
        ConnectionTypeCatalog.ApplyDefaults(profile, OriginalType);
        LoadFromProfile(profile, keepSecrets: true);

        foreach (var issue in ConnectionTypeCatalog.ValidateProfile(profile, requireIncomingSecret, requireOutgoingSecret))
        {
            ModelState.AddModelError(issue.FieldName, issue.Message);
            valid = false;
        }

        return valid;
    }

    protected ConnectionProfile ToProfile()
    {
        return new ConnectionProfile
        {
            Name = Name.Trim(),
            Type = ConnectionTypeCatalog.Normalize(Type),
            CapabilitiesConfigured = true,
            CanRead = CanRead,
            CanWrite = CanWrite,
            IncomingProtocol = ConnectionTypeCatalog.NormalizeIncomingProtocol(IncomingProtocol),
            Host = Host.Trim(),
            Port = Port,
            UseSsl = UseSsl,
            Username = Username.Trim(),
            Secret = Secret,
            OutgoingProtocol = "SMTP",
            OutgoingHost = OutgoingHost.Trim(),
            OutgoingPort = OutgoingPort,
            OutgoingUseSsl = OutgoingUseSsl,
            OutgoingUsername = OutgoingUsername.Trim(),
            OutgoingSecret = OutgoingSecret,
            RemotePath = ConnectionTypeCatalog.NormalizeRemotePath(Type, RemotePath),
            Notes = Notes.Trim()
        };
    }

    protected void LoadFromProfile(ConnectionProfile connection, bool keepSecrets)
    {
        Name = connection.Name;
        Type = ConnectionTypeCatalog.Normalize(connection.Type);
        CanRead = connection.CanRead;
        CanWrite = connection.CanWrite;
        IncomingProtocol = ConnectionTypeCatalog.NormalizeIncomingProtocol(connection.IncomingProtocol);
        Host = connection.Host;
        Port = connection.Port;
        UseSsl = connection.UseSsl;
        Username = connection.Username;
        if (keepSecrets)
        {
            Secret = connection.Secret;
            OutgoingSecret = connection.OutgoingSecret;
        }

        OutgoingHost = connection.OutgoingHost;
        OutgoingPort = connection.OutgoingPort;
        OutgoingUseSsl = connection.OutgoingUseSsl;
        OutgoingUsername = connection.OutgoingUsername;
        RemotePath = ConnectionTypeCatalog.NormalizeRemotePath(connection.Type, connection.RemotePath);
        Notes = connection.Notes;
    }
}
