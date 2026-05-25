using Matarchive.Web.Domain;
using Matarchive.Web.Infrastructure;
using Matarchive.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MatarchiveOptions>(builder.Configuration.GetSection("Matarchive"));

var dataPath = ResolveDataPath(
    builder.Configuration["Matarchive:DataPath"],
    builder.Environment.ContentRootPath);
var keyDirectory = Path.Combine(dataPath, "dp-keys");
Directory.CreateDirectory(keyDirectory);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
    .SetApplicationName("Matarchive");

builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasherExtensions>();
builder.Services.AddSingleton<MatarchiveRepository>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<ApiKeyService>();
builder.Services.AddSingleton<TaskExecutionQueue>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<SmbClientTransferService>();
builder.Services.AddHostedService<TaskRunnerWorker>();
builder.Services.AddHostedService<TaskScheduleScanner>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/account/login";
        options.LogoutPath = "/account/logout";
        options.AccessDeniedPath = "/account/login";
        options.Cookie.Name = ".Matarchive.Auth";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.Cookie.MaxAge = options.ExpireTimeSpan;
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole(MatarchiveConstants.AdminRole)
        .Build();
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AllowAnonymousToPage("/Index");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToFolder("/Account");
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var repository = scope.ServiceProvider.GetRequiredService<MatarchiveRepository>();
    await repository.InitializeAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/status/{taskId:guid?}", async (
    HttpContext httpContext,
    Guid? taskId,
    MatarchiveRepository repository,
    ApiKeyService apiKeyService) =>
{
    var apiKey = await apiKeyService.AuthenticateAsync(httpContext.Request);
    if (apiKey is null)
    {
        return Results.Unauthorized();
    }

    var tasks = await repository.GetTasksAsync();
    var connections = await repository.GetConnectionsAsync();
    var keys = await repository.GetApiKeysAsync();

    var selectedTasks = taskId.HasValue
        ? tasks.Where(task => task.Id == taskId.Value).ToList()
        : tasks;

    var result = new ApiStatusResponse
    {
        GeneratedAt = DateTimeOffset.UtcNow,
        BaseUrl = builder.Configuration["Matarchive:BaseUrl"] ?? "",
        TaskCount = tasks.Count,
        ConnectionCount = connections.Count,
        ApiKeyCount = keys.Count,
        Tasks = selectedTasks.Select(task =>
        {
            var source = connections.FirstOrDefault(connection => connection.Id == task.SourceConnectionId);
            var destination = connections.FirstOrDefault(connection => connection.Id == task.DestinationConnectionId);
            return new ApiTaskStatusDto
            {
                TaskId = task.Id,
                Name = task.Name,
                TaskType = task.TaskType,
                Status = task.LastStatus,
                LastRunAt = task.LastRunAt,
                LastMessage = task.LastMessage,
                Source = FormatConnectionForApi(source),
                Destination = FormatConnectionForApi(destination),
                Enabled = task.Enabled
            };
        }).ToList()
    };

    return Results.Ok(result);
});

app.MapRazorPages();

await app.RunAsync();

static string ResolveDataPath(string? configuredPath, string contentRootPath)
{
    var path = string.IsNullOrWhiteSpace(configuredPath) ? "data" : configuredPath.Trim();
    return Path.IsPathRooted(path)
        ? Path.GetFullPath(path)
        : Path.GetFullPath(Path.Combine(contentRootPath, path));
}

static string FormatConnectionForApi(ConnectionProfile? connection)
{
    if (connection is null)
    {
        return "Unknown (-)";
    }

    var descriptor = ConnectionTypeCatalog.GetDescriptor(connection.Type);
    return $"{connection.Name} ({descriptor.DisplayName}, {ConnectionTypeCatalog.GetCapabilitySummary(connection)})";
}
