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
    public async Task ProcessAsync_WithInviteCode_RendersConfirmationScreen()
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

        await processor.ProcessAsync(BuildMessageUpdate(1, 222, invite.InviteCode), CancellationToken.None);

        var screen = Assert.Single(sender.ScreenMessages);
        Assert.False(screen.WasEdited);
        Assert.Contains("Вы хотите подписаться на уведомления от сервиса <b>Orders</b>?", screen.Text);
        Assert.DoesNotContain("внешний ключ", screen.Text, StringComparison.OrdinalIgnoreCase);
        var markup = Assert.IsType<InlineKeyboardMarkup>(screen.ReplyMarkup);
        Assert.Contains(markup.InlineKeyboard.SelectMany(x => x), x => x.CallbackData == $"sub:yes:{invite.InviteCode}");
        Assert.Contains(markup.InlineKeyboard.SelectMany(x => x), x => x.CallbackData == $"sub:no:{invite.InviteCode}");

        var state = await administration.GetBotScreenStateAsync(222, CancellationToken.None);
        Assert.Equal(222, state.LastBotChatId);
        Assert.Equal(screen.MessageId, state.LastBotMessageId);
    }

    [Fact]
    public async Task ProcessAsync_SecondCommand_CreatesNewScreen()
    {
        var sender = new FakeTelegramMessageSender();
        var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        using var scope = provider.CreateScope();
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
        var administration = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();
        var processor = CreateProcessor(scope, sender);

        await processor.ProcessAsync(BuildMessageUpdate(1, 222, "/start"), CancellationToken.None);

        var service = await administration.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await administration.CreateInviteAsync(777, service.PublicId, Notixa.Api.Domain.Enums.InviteCodeType.General, null, 1, 24, CancellationToken.None);

        await processor.ProcessAsync(BuildMessageUpdate(2, 222, invite.InviteCode), CancellationToken.None);

        Assert.Equal(2, sender.ScreenMessages.Count);
        Assert.False(sender.ScreenMessages[0].WasEdited);
        Assert.False(sender.ScreenMessages[1].WasEdited);
        Assert.NotEqual(sender.ScreenMessages[0].MessageId, sender.ScreenMessages[1].MessageId);
    }

    [Fact]
    public async Task ProcessAsync_CallbackYes_CreatesSubscriptionAndEditsScreen()
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

        await processor.ProcessAsync(BuildMessageUpdate(1, 222, invite.InviteCode), CancellationToken.None);
        var messageId = sender.ScreenMessages[^1].MessageId;

        await processor.ProcessAsync(BuildCallbackUpdate(2, 222, messageId, $"sub:yes:{invite.InviteCode}", "cb-1"), CancellationToken.None);

        var subscriptions = await administration.GetSubscriptionsAsync(222, CancellationToken.None);
        Assert.Single(subscriptions);
        var screen = sender.ScreenMessages[^1];
        Assert.True(screen.WasEdited);
        Assert.Contains("✅ Подписка подключена", screen.Text);
        Assert.Contains("/unsubscribe", screen.Text);
        Assert.DoesNotContain("внешний ключ", screen.Text, StringComparison.OrdinalIgnoreCase);
        var markup = Assert.IsType<InlineKeyboardMarkup>(screen.ReplyMarkup);
        Assert.Contains(markup.InlineKeyboard.SelectMany(x => x), x => x.CallbackData == $"unsub:ask:{service.PublicId}");
    }

    [Fact]
    public async Task ProcessAsync_CallbackNo_DoesNotCreateSubscriptionAndEditsScreen()
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

        await processor.ProcessAsync(BuildMessageUpdate(1, 222, invite.InviteCode), CancellationToken.None);
        var messageId = sender.ScreenMessages[^1].MessageId;

        await processor.ProcessAsync(BuildCallbackUpdate(2, 222, messageId, $"sub:no:{invite.InviteCode}", "cb-2"), CancellationToken.None);

        var subscriptions = await administration.GetSubscriptionsAsync(222, CancellationToken.None);
        Assert.Empty(subscriptions);
        Assert.Contains("Подписка отменена", sender.ScreenMessages[^1].Text);
        Assert.True(sender.ScreenMessages[^1].WasEdited);
    }

    [Fact]
    public async Task ProcessAsync_SubscriptionsCommand_ShowsUnsubscribeButtonsWithoutExternalKey()
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

        await processor.ProcessAsync(BuildMessageUpdate(1, 222, "/subscriptions"), CancellationToken.None);

        var screen = Assert.Single(sender.ScreenMessages);
        Assert.Contains("<b>Orders</b>", screen.Text);
        Assert.Contains($"/unsubscribe {service.PublicId}", screen.Text);
        Assert.DoesNotContain("внешний ключ", screen.Text, StringComparison.OrdinalIgnoreCase);
        var markup = Assert.IsType<InlineKeyboardMarkup>(screen.ReplyMarkup);
        Assert.Contains(markup.InlineKeyboard.SelectMany(x => x), x => x.Text == "🗑️ Отписаться от Orders");
        Assert.Contains(markup.InlineKeyboard.SelectMany(x => x), x => x.CallbackData == $"unsub:ask:{service.PublicId}");
    }

    [Fact]
    public async Task ProcessAsync_WhenAlreadySubscribed_ShowsReadyUnsubscribeActionWithoutExternalKey()
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

        await processor.ProcessAsync(BuildMessageUpdate(1, 222, invite.InviteCode), CancellationToken.None);

        var screen = Assert.Single(sender.ScreenMessages);
        Assert.Contains("Вы уже подписаны", screen.Text);
        Assert.Contains($"/unsubscribe {service.PublicId}", screen.Text);
        Assert.DoesNotContain("внешний ключ", screen.Text, StringComparison.OrdinalIgnoreCase);
        var markup = Assert.IsType<InlineKeyboardMarkup>(screen.ReplyMarkup);
        Assert.Contains(markup.InlineKeyboard.SelectMany(x => x), x => x.CallbackData == $"unsub:ask:{service.PublicId}");
    }

    [Fact]
    public async Task ProcessAsync_UnsubscribeFlow_AsksConfirmationAndRemovesSubscription()
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

        await processor.ProcessAsync(BuildMessageUpdate(1, 222, "/subscriptions"), CancellationToken.None);
        var messageId = sender.ScreenMessages[^1].MessageId;

        await processor.ProcessAsync(BuildCallbackUpdate(2, 222, messageId, $"unsub:ask:{service.PublicId}", "cb-3"), CancellationToken.None);
        Assert.Contains("Вы хотите отписаться от сервиса <b>Orders</b>?", sender.ScreenMessages[^1].Text);
        var confirmMarkup = Assert.IsType<InlineKeyboardMarkup>(sender.ScreenMessages[^1].ReplyMarkup);
        Assert.Contains(confirmMarkup.InlineKeyboard.SelectMany(x => x), x => x.CallbackData == $"unsub:yes:{service.PublicId}");
        Assert.Contains(confirmMarkup.InlineKeyboard.SelectMany(x => x), x => x.CallbackData == $"unsub:no:{service.PublicId}");

        await processor.ProcessAsync(BuildCallbackUpdate(3, 222, messageId, $"unsub:yes:{service.PublicId}", "cb-4"), CancellationToken.None);

        var subscriptions = await administration.GetSubscriptionsAsync(222, CancellationToken.None);
        Assert.Empty(subscriptions);
        Assert.Contains("Подписка отключена", sender.ScreenMessages[^1].Text);
        Assert.True(sender.ScreenMessages[^1].WasEdited);
    }

    [Fact]
    public async Task ProcessAsync_UnsubscribeCancel_ReturnsToSubscriptionsScreen()
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

        await processor.ProcessAsync(BuildMessageUpdate(1, 222, "/subscriptions"), CancellationToken.None);
        var messageId = sender.ScreenMessages[^1].MessageId;

        await processor.ProcessAsync(BuildCallbackUpdate(2, 222, messageId, $"unsub:ask:{service.PublicId}", "cb-5"), CancellationToken.None);
        await processor.ProcessAsync(BuildCallbackUpdate(3, 222, messageId, $"unsub:no:{service.PublicId}", "cb-6"), CancellationToken.None);

        var subscriptions = await administration.GetSubscriptionsAsync(222, CancellationToken.None);
        Assert.Single(subscriptions);
        Assert.Contains("📬 Мои подписки", sender.ScreenMessages[^1].Text);
        Assert.True(sender.ScreenMessages[^1].WasEdited);
    }

    private static Update BuildMessageUpdate(int updateId, long telegramUserId, string text) =>
        new()
        {
            Id = updateId,
            Message = new Message
            {
                Date = DateTime.UtcNow,
                Text = text,
                Chat = new Chat { Id = telegramUserId, Type = ChatType.Private },
                From = new User { Id = telegramUserId, FirstName = "Test" }
            }
        };

    private static Update BuildCallbackUpdate(int updateId, long telegramUserId, int messageId, string data, string callbackId) =>
        new()
        {
            Id = updateId,
            CallbackQuery = new CallbackQuery
            {
                Id = callbackId,
                Data = data,
                From = new User { Id = telegramUserId, FirstName = "Test" },
                Message = new Message
                {
                    Id = messageId,
                    Date = DateTime.UtcNow,
                    Chat = new Chat { Id = telegramUserId, Type = ChatType.Private }
                }
            }
        };

    private static TelegramUpdateProcessor CreateProcessor(IServiceScope scope, FakeTelegramMessageSender sender) =>
        new(
            scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>(),
            scope.ServiceProvider.GetRequiredService<IUserContextService>(),
            Options.Create(new TelegramBotOptions { BotUsername = "Notixa_bot" }),
            new FakeBotClientAccessor(),
            sender,
            NullLogger<TelegramUpdateProcessor>.Instance);
}
