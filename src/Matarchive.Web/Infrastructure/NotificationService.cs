using System.Net;
using System.Net.Mail;
using System.Text;
using Matarchive.Web.Domain;
using Microsoft.Extensions.Options;

namespace Matarchive.Web.Infrastructure;

public sealed class NotificationService
{
    private readonly MatarchiveRepository _repository;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(MatarchiveRepository repository, ILogger<NotificationService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task TrySendAsync(
        TaskDefinition task,
        TaskRun run,
        ConnectionProfile? source,
        ConnectionProfile? destination,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await _repository.GetNotificationSettingsAsync(cancellationToken);
            var shouldSend = run.Status == "Succeeded" ? settings.NotifyOnSuccess : settings.NotifyOnFailure;
            if (!shouldSend)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(settings.SmtpHost) ||
                string.IsNullOrWhiteSpace(settings.FromAddress) ||
                string.IsNullOrWhiteSpace(settings.RecipientsCsv))
            {
                return;
            }

            var recipients = settings.RecipientsCsv
                .Split([';', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (recipients.Length == 0)
            {
                return;
            }

            using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                EnableSsl = settings.UseSsl,
                Credentials = string.IsNullOrWhiteSpace(settings.Username)
                    ? CredentialCache.DefaultNetworkCredentials
                    : new NetworkCredential(settings.Username, settings.Password)
            };

            using var message = new MailMessage
            {
                From = new MailAddress(settings.FromAddress, string.IsNullOrWhiteSpace(settings.FromName) ? "Matarchive" : settings.FromName),
                Subject = $"Matarchive {task.TaskType} - {task.Name}: {run.Status}",
                Body = BuildBody(task, run, source, destination),
                IsBodyHtml = false
            };

            foreach (var recipient in recipients)
            {
                message.To.Add(recipient);
            }

            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send notification for task {TaskId}", task.Id);
        }
    }

    private static string BuildBody(TaskDefinition task, TaskRun run, ConnectionProfile? source, ConnectionProfile? destination)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Matarchive task notification");
        builder.AppendLine();
        builder.AppendLine($"Task: {task.Name}");
        builder.AppendLine($"Type: {task.TaskType}");
        builder.AppendLine($"Run status: {run.Status}");
        builder.AppendLine($"Trigger: {run.Trigger}");
        builder.AppendLine($"Source: {source?.Name ?? "Unknown"} ({source?.Type ?? "-"})");
        builder.AppendLine($"Destination: {destination?.Name ?? "Unknown"} ({destination?.Type ?? "-"})");
        builder.AppendLine($"Finished: {run.FinishedAt:O}");
        builder.AppendLine();
        builder.AppendLine(run.Message);
        return builder.ToString();
    }
}

