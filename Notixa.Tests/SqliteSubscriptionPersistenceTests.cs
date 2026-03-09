using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notixa.Api.Data;
using Notixa.Api.Options;
using Notixa.Api.Domain.Enums;
using Notixa.Api.Services;
using Notixa.Tests.TestDoubles;

namespace Notixa.Tests;

public sealed class SqliteSubscriptionPersistenceTests
{
    [Fact]
    public async Task RedeemInviteAsync_PersistsSubscriptionInSqlite()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"notixa-tests-{Guid.NewGuid():N}.db");

        try
        {
            var services = new ServiceCollection();
            var sender = new FakeTelegramMessageSender();
            var time = new FakeTimeProvider(DateTimeOffset.UtcNow);

            services.AddLogging();
            services.Configure<AppSecurityOptions>(options => options.SuperAdminTelegramUserId = 777);
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            services.AddSingleton<ITimeProvider>(time);
            services.AddSingleton<ISecretHasher, Sha256SecretHasher>();
            services.AddSingleton<ISecretGenerator, SecretGenerator>();
            services.AddScoped<IUserContextService, UserContextService>();
            services.AddScoped<IPlatformAdministrationService, PlatformAdministrationService>();
            services.AddScoped<ITemplateRenderer, TemplateRenderer>();
            services.AddSingleton<ITelegramMessageSender>(sender);
            services.AddScoped<INotificationDispatchService, NotificationDispatchService>();
            services.AddScoped<IBotConversationService, BotConversationService>();
            services.AddScoped<StartupDatabaseInitializer>();

            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();

            var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
            await initializer.InitializeAsync(CancellationToken.None);

            var administration = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
            var service = await administration.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
            var invite = await administration.CreateInviteAsync(777, service.PublicId, InviteCodeType.General, null, 5, 24, CancellationToken.None);
            var subscription = await administration.RedeemInviteAsync(973222702, invite.InviteCode, CancellationToken.None);

            Assert.NotNull(subscription);

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var persisted = await dbContext.Subscriptions.SingleAsync(x => x.TelegramUserId == 973222702, CancellationToken.None);
            Assert.Equal(service.PublicId, (await dbContext.Services.FindAsync([persisted.ServiceDefinitionId], CancellationToken.None))!.PublicId);
        }
        finally
        {
            try
            {
                if (File.Exists(databasePath))
                {
                    File.Delete(databasePath);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
