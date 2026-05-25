using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Matarchive.Web.Domain;

namespace Matarchive.Web.Services;

public sealed class SmbClientTransferService
{
    private readonly ILogger<SmbClientTransferService> _logger;

    public SmbClientTransferService(ILogger<SmbClientTransferService> logger)
    {
        _logger = logger;
    }

    public bool IsSmb(ConnectionProfile connection)
    {
        return ConnectionTypeCatalog.IsSmb(connection);
    }

    public string GetRemoteDirectoryDisplayPath(ConnectionProfile connection)
    {
        return ParseLocation(connection).DirectoryDisplayPath;
    }

    public string GetRemoteFileDisplayPath(ConnectionProfile connection, string fileName)
    {
        return ParseLocation(connection).GetFileDisplayPath(fileName);
    }

    public async Task DownloadDirectoryAsync(ConnectionProfile connection, string localDirectory, CancellationToken cancellationToken)
    {
        var location = ParseLocation(connection);
        Directory.CreateDirectory(localDirectory);

        var commands = new List<string>();
        if (!string.IsNullOrWhiteSpace(location.RelativePath))
        {
            commands.Add($"cd {QuoteSmbPath(location.RelativePath)}");
        }

        commands.Add($"lcd {QuoteLocalPath(localDirectory)}");
        commands.Add("recurse");
        commands.Add("prompt off");
        commands.Add("mget *");

        await ExecuteAsync(location, commands, cancellationToken);
    }

    public async Task<string> UploadDirectoryAsync(ConnectionProfile connection, string localDirectory, bool verifyDestination, CancellationToken cancellationToken)
    {
        var location = ParseLocation(connection);
        await EnsureRemoteDirectoryAsync(location, cancellationToken);

        var commands = new List<string>();
        if (!string.IsNullOrWhiteSpace(location.RelativePath))
        {
            commands.Add($"cd {QuoteSmbPath(location.RelativePath)}");
        }

        commands.Add($"lcd {QuoteLocalPath(localDirectory)}");
        commands.Add("recurse");
        commands.Add("prompt off");
        commands.Add("mput *");

        await ExecuteAsync(location, commands, cancellationToken);
        if (verifyDestination)
        {
            await VerifyRemoteDirectoryExistsAsync(location, cancellationToken);
        }

        return location.DirectoryDisplayPath;
    }

    public async Task<string> UploadFileAsync(ConnectionProfile connection, string localFilePath, bool verifyDestination, CancellationToken cancellationToken)
    {
        var location = ParseLocation(connection);
        await EnsureRemoteDirectoryAsync(location, cancellationToken);

        var remoteFileName = Path.GetFileName(localFilePath);
        var tempRemoteFileName = $".matarchive-{Guid.NewGuid():N}-{remoteFileName}.tmp";
        await TryDeleteRemoteFileAsync(location, tempRemoteFileName, cancellationToken);

        var commands = new List<string>();
        if (!string.IsNullOrWhiteSpace(location.RelativePath))
        {
            commands.Add($"cd {QuoteSmbPath(location.RelativePath)}");
        }

        commands.Add($"put {QuoteLocalPath(localFilePath)} {QuoteSmbPath(tempRemoteFileName)}");

        await ExecuteAsync(location, commands, cancellationToken);
        if (verifyDestination)
        {
            await VerifyRemoteFileExistsAsync(location, tempRemoteFileName, cancellationToken);
        }

        await TryDeleteRemoteFileAsync(location, remoteFileName, cancellationToken);
        await RenameRemoteFileAsync(location, tempRemoteFileName, remoteFileName, cancellationToken);
        if (verifyDestination)
        {
            await VerifyRemoteFileExistsAsync(location, remoteFileName, cancellationToken);
        }

        return location.GetFileDisplayPath(remoteFileName);
    }

    public async Task DeleteFileAsync(ConnectionProfile connection, string remoteFileName, CancellationToken cancellationToken)
    {
        var location = ParseLocation(connection);
        await TryDeleteRemoteFileAsync(location, remoteFileName, cancellationToken);
    }

    private async Task EnsureRemoteDirectoryAsync(SmbLocation location, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(location.RelativePath))
        {
            return;
        }

        var current = string.Empty;
        foreach (var segment in location.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            current = string.IsNullOrEmpty(current) ? segment : $"{current}/{segment}";

            if (await TryDirectoryExistsAsync(location, current, cancellationToken))
            {
                continue;
            }

            try
            {
                await ExecuteAsync(location, [$"mkdir {QuoteSmbPath(current)}"], cancellationToken);
            }
            catch (SmbClientException ex) when (IsAlreadyExists(ex))
            {
                // Another run may have created the directory in the meantime.
            }
        }
    }

    private async Task TryDeleteRemoteFileAsync(SmbLocation location, string remoteFileName, CancellationToken cancellationToken)
    {
        try
        {
            var commands = new List<string>();
            if (!string.IsNullOrWhiteSpace(location.RelativePath))
            {
                commands.Add($"cd {QuoteSmbPath(location.RelativePath)}");
            }

            commands.Add($"del {QuoteSmbPath(remoteFileName)}");
            await ExecuteAsync(location, commands, cancellationToken);
        }
        catch (SmbClientException ex) when (IsMissing(ex))
        {
        }
    }

    private async Task RenameRemoteFileAsync(SmbLocation location, string sourceFileName, string targetFileName, CancellationToken cancellationToken)
    {
        var commands = new List<string>();
        if (!string.IsNullOrWhiteSpace(location.RelativePath))
        {
            commands.Add($"cd {QuoteSmbPath(location.RelativePath)}");
        }

        commands.Add($"rename {QuoteSmbPath(sourceFileName)} {QuoteSmbPath(targetFileName)}");
        await ExecuteAsync(location, commands, cancellationToken);
    }

    private async Task VerifyRemoteFileExistsAsync(SmbLocation location, string remoteFileName, CancellationToken cancellationToken)
    {
        var commands = new List<string>();
        if (!string.IsNullOrWhiteSpace(location.RelativePath))
        {
            commands.Add($"cd {QuoteSmbPath(location.RelativePath)}");
        }

        commands.Add($"allinfo {QuoteSmbPath(remoteFileName)}");
        await ExecuteAsync(location, commands, cancellationToken);
    }

    private async Task VerifyRemoteDirectoryExistsAsync(SmbLocation location, CancellationToken cancellationToken)
    {
        var commands = new List<string>();
        if (!string.IsNullOrWhiteSpace(location.RelativePath))
        {
            commands.Add($"cd {QuoteSmbPath(location.RelativePath)}");
        }

        commands.Add("pwd");
        await ExecuteAsync(location, commands, cancellationToken);
    }

    private async Task<bool> TryDirectoryExistsAsync(SmbLocation location, string remotePath, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteAsync(location, [$"cd {QuoteSmbPath(remotePath)}"], cancellationToken);
            return true;
        }
        catch (SmbClientException ex) when (IsMissing(ex))
        {
            return false;
        }
    }

    private async Task<SmbCommandResult> ExecuteAsync(SmbLocation location, IReadOnlyList<string> commands, CancellationToken cancellationToken)
    {
        var commandLine = string.Join("; ", commands);
        _logger.LogDebug("Running smbclient for {RemotePath}: {Command}", location.DirectoryDisplayPath, commandLine);

        var authFile = await CreateAuthFileAsync(location, cancellationToken);
        try
        {
            var startInfo = new ProcessStartInfo("smbclient")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add(location.UncPath);
            startInfo.ArgumentList.Add("-A");
            startInfo.ArgumentList.Add(authFile);
            if (location.Port > 0 && location.Port != 445)
            {
                startInfo.ArgumentList.Add("-p");
                startInfo.ArgumentList.Add(location.Port.ToString(CultureInfo.InvariantCulture));
            }

            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(commandLine);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("smbclient konnte nicht gestartet werden.");
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch
            {
                TryKill(process);
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                throw new SmbClientException(
                    BuildFailureMessage(location, process.ExitCode, stdout, stderr),
                    process.ExitCode,
                    stdout,
                    stderr);
            }

            return new SmbCommandResult(process.ExitCode, stdout, stderr);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("smbclient ist im Container nicht verfuegbar.", ex);
        }
        finally
        {
            TryDeleteFile(authFile);
        }
    }

    private async Task<string> CreateAuthFileAsync(SmbLocation location, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Path.GetTempPath(), "matarchive", "smb");
        Directory.CreateDirectory(directory);

        var authFile = Path.Combine(directory, $"{Guid.NewGuid():N}.auth");
        var username = location.Username;
        var domain = string.Empty;

        var domainSeparator = username.IndexOf('\\');
        if (domainSeparator > 0)
        {
            domain = username[..domainSeparator].Trim();
            username = username[(domainSeparator + 1)..].Trim();
        }

        var lines = new List<string>
        {
            $"username = {username}"
        };

        if (!string.IsNullOrWhiteSpace(domain))
        {
            lines.Add($"domain = {domain}");
        }

        lines.Add($"password = {location.Secret}");

        await File.WriteAllLinesAsync(authFile, lines, cancellationToken);
        return authFile;
    }

    private static SmbLocation ParseLocation(ConnectionProfile connection)
    {
        var normalized = ConnectionTypeCatalog.NormalizeRemotePath(connection.Type, connection.RemotePath)
            .Replace('\\', '/')
            .Trim('/');

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            throw new InvalidOperationException($"Die SMB-Verbindung '{connection.Name}' benoetigt eine Freigabe im Pfad.");
        }

        var share = segments[0];
        var relativePath = segments.Length > 1 ? string.Join('/', segments.Skip(1)) : string.Empty;

        return new SmbLocation(
            connection.Host.Trim(),
            share,
            relativePath,
            connection.Port,
            connection.Username.Trim(),
            connection.Secret);
    }

    private static bool IsMissing(SmbClientException exception)
    {
        return ContainsStatus(exception.CombinedOutput, "NT_STATUS_OBJECT_NAME_NOT_FOUND")
            || ContainsStatus(exception.CombinedOutput, "NT_STATUS_OBJECT_PATH_NOT_FOUND")
            || ContainsStatus(exception.CombinedOutput, "NT_STATUS_NO_SUCH_FILE")
            || ContainsStatus(exception.CombinedOutput, "No such file or directory")
            || ContainsStatus(exception.CombinedOutput, "not found");
    }

    private static bool IsAlreadyExists(SmbClientException exception)
    {
        return ContainsStatus(exception.CombinedOutput, "NT_STATUS_OBJECT_NAME_COLLISION")
            || ContainsStatus(exception.CombinedOutput, "File exists")
            || ContainsStatus(exception.CombinedOutput, "already exists");
    }

    private static bool ContainsStatus(string value, string token)
    {
        return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string QuoteSmbPath(string value)
    {
        var escaped = value.Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static string QuoteLocalPath(string value)
    {
        return QuoteSmbPath(value);
    }

    private static string BuildFailureMessage(SmbLocation location, int exitCode, string stdout, string stderr)
    {
        var details = string.Join(
            " ",
            new[] { stderr.Trim(), stdout.Trim() }.Where(value => !string.IsNullOrWhiteSpace(value)));

        if (details.Length > 900)
        {
            details = $"{details[..900]}...";
        }

        return string.IsNullOrWhiteSpace(details)
            ? $"smbclient fehlgeschlagen fuer {location.DirectoryDisplayPath} mit Exit-Code {exitCode}."
            : $"smbclient fehlgeschlagen fuer {location.DirectoryDisplayPath} mit Exit-Code {exitCode}: {details}";
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    private sealed record SmbLocation(
        string Host,
        string Share,
        string RelativePath,
        int Port,
        string Username,
        string Secret)
    {
        public string UncPath => $"//{Host}/{Share}";

        public string DirectoryDisplayPath => string.IsNullOrWhiteSpace(RelativePath)
            ? UncPath
            : $"{UncPath}/{RelativePath}";

        public string GetFileDisplayPath(string fileName)
        {
            return string.IsNullOrWhiteSpace(RelativePath)
                ? $"{UncPath}/{fileName}"
                : $"{UncPath}/{RelativePath}/{fileName}";
        }
    }

    private sealed record SmbCommandResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class SmbClientException : InvalidOperationException
    {
        public SmbClientException(string message, int exitCode, string standardOutput, string standardError)
            : base(message)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput;
            StandardError = standardError;
        }

        public int ExitCode { get; }

        public string StandardOutput { get; }

        public string StandardError { get; }

        public string CombinedOutput => $"{StandardError}\n{StandardOutput}";
    }
}
