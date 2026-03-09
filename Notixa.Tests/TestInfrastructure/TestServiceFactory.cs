using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notixa.Api.Data;
using Notixa.Api.Options;
using Notixa.Api.Services;
using Notixa.Tests.TestDoubles;

namespace Notixa.Tests.TestInfrastructure;

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
        services.AddScoped<IBotConversationService, BotConversationService>();
        services.AddScoped<StartupDatabaseInitializer>();
        return services.BuildServiceProvider();
    }
}
