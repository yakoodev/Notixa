using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelegramNotifications.Api.Data;
using TelegramNotifications.Api.Options;
using TelegramNotifications.Api.Services;
using TelegramNotifications.Tests.TestDoubles;

namespace TelegramNotifications.Tests.TestInfrastructure;

public static class TestServiceFactory
{
    public static ServiceProvider Create(long superAdminTelegramUserId, FakeTelegramMessageSender sender, FakeTimeProvider timeProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<AppSecurityOptions>(options => options.SuperAdminTelegramUserId = superAdminTelegramUserId);
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString("N")));
        services.AddSingleton<ITimeProvider>(timeProvider);
        services.AddSingleton<ISecretHasher, Sha256SecretHasher>();
        services.AddSingleton<ISecretGenerator, SecretGenerator>();
        services.AddScoped<IUserContextService, UserContextService>();
        services.AddScoped<IPlatformAdministrationService, PlatformAdministrationService>();
        services.AddScoped<ITemplateRenderer, TemplateRenderer>();
        services.AddSingleton<ITelegramMessageSender>(sender);
        services.AddScoped<INotificationDispatchService, NotificationDispatchService>();
        services.AddScoped<StartupDatabaseInitializer>();
        return services.BuildServiceProvider();
    }
}
