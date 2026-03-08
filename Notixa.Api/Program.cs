using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Notixa.Api.Data;
using Notixa.Api.Options;
using Notixa.Api.Services;
using Notixa.Api.Telegram;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<TelegramBotOptions>(builder.Configuration.GetSection(TelegramBotOptions.SectionName));
builder.Services.Configure<AppSecurityOptions>(builder.Configuration.GetSection(AppSecurityOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
{
    var storageOptions = serviceProvider
        .GetRequiredService<IConfiguration>()
        .GetSection(StorageOptions.SectionName)
        .Get<StorageOptions>() ?? new StorageOptions();

    var resolvedConnectionString = ResolveConnectionString(storageOptions.ConnectionString);
    var sqliteBuilder = new SqliteConnectionStringBuilder(resolvedConnectionString);
    var dataDirectory = Path.GetDirectoryName(sqliteBuilder.DataSource);
    if (!string.IsNullOrWhiteSpace(dataDirectory))
    {
        Directory.CreateDirectory(dataDirectory);
    }

    options.UseSqlite(resolvedConnectionString);
});

builder.Services.AddSingleton<ITimeProvider, SystemTimeProvider>();
builder.Services.AddSingleton<ISecretHasher, Sha256SecretHasher>();
builder.Services.AddSingleton<ISecretGenerator, SecretGenerator>();
builder.Services.AddScoped<ITemplateRenderer, TemplateRenderer>();
builder.Services.AddScoped<IUserContextService, UserContextService>();
builder.Services.AddScoped<IPlatformAdministrationService, PlatformAdministrationService>();
builder.Services.AddScoped<INotificationDispatchService, NotificationDispatchService>();
builder.Services.AddScoped<ITelegramUpdateProcessor, TelegramUpdateProcessor>();
builder.Services.AddScoped<StartupDatabaseInitializer>();
builder.Services.AddSingleton<IBotClientAccessor, BotClientAccessor>();
builder.Services.AddScoped<ITelegramMessageSender, TelegramMessageSender>();

builder.Services.AddHostedService<DatabaseInitializationHostedService>();
builder.Services.AddHostedService<TelegramPollingHostedService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy" }));
app.MapControllers();

app.Run();

static string ResolveConnectionString(string connectionString)
{
    var builder = new SqliteConnectionStringBuilder(connectionString);
    if (string.IsNullOrWhiteSpace(builder.DataSource) || Path.IsPathRooted(builder.DataSource))
    {
        return builder.ToString();
    }

    builder.DataSource = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, builder.DataSource));
    return builder.ToString();
}

public partial class Program;
