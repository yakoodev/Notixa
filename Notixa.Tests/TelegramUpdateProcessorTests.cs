using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Notixa.Api.Options;
using Notixa.Api.Services;
using Notixa.Api.Telegram;
using Notixa.Tests.TestDoubles;
using Notixa.Tests.TestInfrastructure;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Notixa.Tests;

public sealed class TelegramUpdateProcessorTests
{
    [Fact]
    public async Task ProcessAsync_WithInviteCode_SendsConfirmationButtons()
    {
        var sender = new FakeTelegramMessageSender();
        var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var administration = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var processor = CreateProcessor(scope, sender);

        var service = await administration.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await administration.CreateInviteAsync(777, service.PublicId, Notixa.Api.Domain.Enums.InviteCodeType.General, null, 1, 24, CancellationToken.None);

        await processor.ProcessAsync(new Update
        {
            Id = 1,
            Message = new Message
            {
                Date = DateTime.UtcNow,
                Text = invite.InviteCode,
                Chat = new Chat { Id = 222, Type = ChatType.Private },
                From = new User { Id = 222, FirstName = "Test" }
            }
        }, CancellationToken.None);

        var message = Assert.Single(sender.SentMessages);
        Assert.Contains("Вы хотите подписаться на уведомления от сервиса <b>Orders</b>?", message.Text);
        Assert.DoesNotContain("внешний ключ", message.Text, StringComparison.OrdinalIgnoreCase);
        var markup = Assert.IsType<InlineKeyboardMarkup>(message.ReplyMarkup);
        Assert.Contains(markup.InlineKeyboard.SelectMany(x => x), x => x.CallbackData == $"sub:yes:{invite.InviteCode}");
        Assert.Contains(markup.InlineKeyboard.SelectMany(x => x), x => x.CallbackData == $"sub:no:{invite.InviteCode}");
    }

    [Fact]
    public async Task ProcessAsync_CallbackYes_CreatesSubscription()
    {
        var sender = new FakeTelegramMessageSender();
        var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var administration = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var processor = CreateProcessor(scope, sender);

        var service = await administration.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await administration.CreateInviteAsync(777, service.PublicId, Notixa.Api.Domain.Enums.InviteCodeType.General, null, 1, 24, CancellationToken.None);

        await processor.ProcessAsync(new Update
        {
            Id = 2,
            CallbackQuery = new CallbackQuery
            {
                Id = "cb-1",
                Data = $"sub:yes:{invite.InviteCode}",
                From = new User { Id = 222, FirstName = "Test" },
                Message = new Message
                {
                    Date = DateTime.UtcNow,
                    Chat = new Chat { Id = 222, Type = ChatType.Private }
                }
            }
        }, CancellationToken.None);

        var subscriptions = await administration.GetSubscriptionsAsync(222, CancellationToken.None);
        Assert.Single(subscriptions);
        var message = Assert.Single(sender.SentMessages);
        Assert.Contains("✅ Подписка подключена", message.Text);
        Assert.Contains("/unsubscribe", message.Text);
        Assert.DoesNotContain("внешний ключ", message.Text, StringComparison.OrdinalIgnoreCase);
        var markup = Assert.IsType<InlineKeyboardMarkup>(message.ReplyMarkup);
        Assert.Contains(markup.InlineKeyboard.SelectMany(x => x), x => x.CallbackData == $"unsub:{service.PublicId}");
    }

    [Fact]
    public async Task ProcessAsync_CallbackNo_DoesNotCreateSubscription()
    {
        var sender = new FakeTelegramMessageSender();
        var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var administration = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var processor = CreateProcessor(scope, sender);

        var service = await administration.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await administration.CreateInviteAsync(777, service.PublicId, Notixa.Api.Domain.Enums.InviteCodeType.General, null, 1, 24, CancellationToken.None);

        await processor.ProcessAsync(new Update
        {
            Id = 3,
            CallbackQuery = new CallbackQuery
            {
                Id = "cb-2",
                Data = $"sub:no:{invite.InviteCode}",
                From = new User { Id = 222, FirstName = "Test" },
                Message = new Message
                {
                    Date = DateTime.UtcNow,
                    Chat = new Chat { Id = 222, Type = ChatType.Private }
                }
            }
        }, CancellationToken.None);

        var subscriptions = await administration.GetSubscriptionsAsync(222, CancellationToken.None);
        Assert.Empty(subscriptions);
        Assert.Contains(sender.SentMessages, x => x.Text.Contains("Подписка отменена"));
    }

    [Fact]
    public async Task ProcessAsync_SubscriptionsCommand_ShowsReadyUnsubscribeCommandWithoutExternalKey()
    {
        var sender = new FakeTelegramMessageSender();
        var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var administration = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var processor = CreateProcessor(scope, sender);

        var service = await administration.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await administration.CreateInviteAsync(777, service.PublicId, Notixa.Api.Domain.Enums.InviteCodeType.Personal, "user-2", 1, 24, CancellationToken.None);
        await administration.RedeemInviteAsync(222, invite.InviteCode, CancellationToken.None);

        await processor.ProcessAsync(new Update
        {
            Id = 4,
            Message = new Message
            {
                Date = DateTime.UtcNow,
                Text = "/subscriptions",
                Chat = new Chat { Id = 222, Type = ChatType.Private },
                From = new User { Id = 222, FirstName = "Test" }
            }
        }, CancellationToken.None);

        var message = Assert.Single(sender.SentMessages);
        Assert.Contains("<b>Orders</b>", message.Text);
        Assert.Contains($"/unsubscribe {service.PublicId}", message.Text);
        Assert.DoesNotContain("внешний ключ", message.Text, StringComparison.OrdinalIgnoreCase);
        var markup = Assert.IsType<InlineKeyboardMarkup>(message.ReplyMarkup);
        Assert.Contains(markup.InlineKeyboard.SelectMany(x => x), x => x.CallbackData == $"unsub:{service.PublicId}");
    }

    [Fact]
    public async Task ProcessAsync_WhenAlreadySubscribed_ShowsReadyUnsubscribeCommandWithoutExternalKey()
    {
        var sender = new FakeTelegramMessageSender();
        var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var administration = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var processor = CreateProcessor(scope, sender);

        var service = await administration.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await administration.CreateInviteAsync(777, service.PublicId, Notixa.Api.Domain.Enums.InviteCodeType.Personal, "user-2", 1, 24, CancellationToken.None);
        await administration.RedeemInviteAsync(222, invite.InviteCode, CancellationToken.None);

        await processor.ProcessAsync(new Update
        {
            Id = 5,
            Message = new Message
            {
                Date = DateTime.UtcNow,
                Text = invite.InviteCode,
                Chat = new Chat { Id = 222, Type = ChatType.Private },
                From = new User { Id = 222, FirstName = "Test" }
            }
        }, CancellationToken.None);

        var message = Assert.Single(sender.SentMessages);
        Assert.Contains("Вы уже подписаны", message.Text);
        Assert.Contains($"/unsubscribe {service.PublicId}", message.Text);
        Assert.DoesNotContain("внешний ключ", message.Text, StringComparison.OrdinalIgnoreCase);
        var markup = Assert.IsType<InlineKeyboardMarkup>(message.ReplyMarkup);
        Assert.Contains(markup.InlineKeyboard.SelectMany(x => x), x => x.CallbackData == $"unsub:{service.PublicId}");
    }

    [Fact]
    public async Task ProcessAsync_UnsubscribeCallback_RemovesSubscription()
    {
        var sender = new FakeTelegramMessageSender();
        var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var administration = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var processor = CreateProcessor(scope, sender);

        var service = await administration.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await administration.CreateInviteAsync(777, service.PublicId, Notixa.Api.Domain.Enums.InviteCodeType.General, null, 1, 24, CancellationToken.None);
        await administration.RedeemInviteAsync(222, invite.InviteCode, CancellationToken.None);

        await processor.ProcessAsync(new Update
        {
            Id = 6,
            CallbackQuery = new CallbackQuery
            {
                Id = "cb-3",
                Data = $"unsub:{service.PublicId}",
                From = new User { Id = 222, FirstName = "Test" },
                Message = new Message
                {
                    Date = DateTime.UtcNow,
                    Chat = new Chat { Id = 222, Type = ChatType.Private }
                }
            }
        }, CancellationToken.None);

        var subscriptions = await administration.GetSubscriptionsAsync(222, CancellationToken.None);
        Assert.Empty(subscriptions);
        var message = Assert.Single(sender.SentMessages);
        Assert.Contains("Подписка отключена", message.Text);
    }

    private static TelegramUpdateProcessor CreateProcessor(IServiceScope scope, FakeTelegramMessageSender sender) =>
        new(
            scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>(),
            scope.ServiceProvider.GetRequiredService<IUserContextService>(),
            Options.Create(new TelegramBotOptions { BotUsername = "Notixa_bot" }),
            new FakeBotClientAccessor(),
            sender,
            NullLogger<TelegramUpdateProcessor>.Instance);
}
