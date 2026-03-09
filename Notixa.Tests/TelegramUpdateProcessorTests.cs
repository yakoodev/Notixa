using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Notixa.Api.Data;
using Notixa.Api.Domain.Enums;
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
    public async Task ProcessAsync_StartCommand_RendersButtonFirstHomeScreen()
    {
        var sender = new FakeTelegramMessageSender();
        await using var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await using var scope = provider.CreateAsyncScope();
        await InitializeAsync(scope);
        var processor = CreateProcessor(scope, sender);

        await processor.ProcessAsync(BuildMessageUpdate(1, 222, "/start"), CancellationToken.None);

        var screen = Assert.Single(sender.ScreenMessages);
        Assert.Contains("кнопки", screen.Text, StringComparison.OrdinalIgnoreCase);
        var markup = Assert.IsType<InlineKeyboardMarkup>(screen.ReplyMarkup);
        Assert.Contains(markup.InlineKeyboard.SelectMany(x => x), x => x.CallbackData == BotCallbackKeys.Subscriptions);
    }

    [Theory]
    [InlineData("/start inv_123")]
    [InlineData("/start@NotixaBot")]
    public async Task ProcessAsync_StartVariants_AreHandledAsHomeEntryPoint(string text)
    {
        var sender = new FakeTelegramMessageSender();
        await using var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await using var scope = provider.CreateAsyncScope();
        await InitializeAsync(scope);
        var processor = CreateProcessor(scope, sender);

        await processor.ProcessAsync(BuildMessageUpdate(1, 222, text), CancellationToken.None);

        var screen = Assert.Single(sender.ScreenMessages);
        Assert.Contains("Notixa", screen.Text);
    }

    [Fact]
    public async Task ProcessAsync_StartCommand_IgnoresSavedPreviousScreenAndSendsNewMessage()
    {
        var sender = new FakeTelegramMessageSender();
        await using var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await using var scope = provider.CreateAsyncScope();
        await InitializeAsync(scope);
        var processor = CreateProcessor(scope, sender);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        await admin.RegisterOrUpdateUserAsync(new User { Id = 222, FirstName = "Test" }, CancellationToken.None);
        await admin.SaveBotScreenStateAsync(222, 222, 999, CancellationToken.None);

        await processor.ProcessAsync(BuildMessageUpdate(1, 222, "/start"), CancellationToken.None);

        var screen = Assert.Single(sender.ScreenMessages);
        Assert.False(screen.WasEdited);
        Assert.NotEqual(999, screen.MessageId);
    }

    [Fact]
    public async Task ProcessAsync_CreateServiceFlow_CreatesServiceWithoutSlashCommand()
    {
        var sender = new FakeTelegramMessageSender();
        await using var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await using var scope = provider.CreateAsyncScope();
        await InitializeAsync(scope);
        var processor = CreateProcessor(scope, sender);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        await processor.ProcessAsync(BuildCallbackUpdate(1, 777, 100, BotCallbackKeys.FlowStartCreateService, "cb-1"), CancellationToken.None);
        await processor.ProcessAsync(BuildMessageUpdate(2, 777, "Orders"), CancellationToken.None);
        await processor.ProcessAsync(BuildMessageUpdate(3, 777, "Order notifications"), CancellationToken.None);
        await processor.ProcessAsync(BuildCallbackUpdate(4, 777, 100, BotCallbackKeys.FlowCreateServiceConfirm, "cb-2"), CancellationToken.None);

        var services = await admin.GetManagedServicesAsync(777, CancellationToken.None);
        var created = Assert.Single(services);
        Assert.Equal("Orders", created.Name);
        Assert.Contains("Название: <b>Orders</b>", sender.ScreenMessages[1].Text);
        Assert.Contains("Сервис создан", sender.ScreenMessages[^1].Text);
        Assert.True(sender.ScreenMessages[1].WasEdited);
        Assert.True(sender.ScreenMessages[2].WasEdited);
        Assert.True(sender.ScreenMessages[3].WasEdited);
        Assert.Single(sender.ScreenMessages.Select(x => x.MessageId).Distinct());
    }

    [Fact]
    public async Task ProcessAsync_CreateTemplateFlow_UsesButtonsAndMessageInput()
    {
        var sender = new FakeTelegramMessageSender();
        await using var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await using var scope = provider.CreateAsyncScope();
        await InitializeAsync(scope);
        var processor = CreateProcessor(scope, sender);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        var service = await admin.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);

        await processor.ProcessAsync(BuildCallbackUpdate(1, 777, 100, $"{BotCallbackKeys.FlowStartCreateTemplatePrefix}{service.PublicId}", "cb-1"), CancellationToken.None);
        await processor.ProcessAsync(BuildCallbackUpdate(2, 777, 100, $"{BotCallbackKeys.FlowTemplateParseModePrefix}{TemplateParseMode.Html}", "cb-2"), CancellationToken.None);
        await processor.ProcessAsync(BuildMessageUpdate(3, 777, "build-failed"), CancellationToken.None);
        await processor.ProcessAsync(BuildMessageUpdate(4, 777, "<b>Hello</b> {{name}}"), CancellationToken.None);

        var serviceResult = await admin.GetManagedServicesAsync(777, CancellationToken.None);
        Assert.Single(serviceResult);
        Assert.Contains("Шаблон сохранен", sender.ScreenMessages[^1].Text);
    }

    [Fact]
    public async Task ProcessAsync_PersonalInviteFlow_CreatesInviteAfterManualExpiry()
    {
        var sender = new FakeTelegramMessageSender();
        await using var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await using var scope = provider.CreateAsyncScope();
        await InitializeAsync(scope);
        var processor = CreateProcessor(scope, sender);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        var service = await admin.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);

        await processor.ProcessAsync(BuildCallbackUpdate(1, 777, 100, $"{BotCallbackKeys.FlowStartPersonalInvitePrefix}{service.PublicId}", "cb-1"), CancellationToken.None);
        await processor.ProcessAsync(BuildMessageUpdate(2, 777, "customer-42"), CancellationToken.None);
        await processor.ProcessAsync(BuildCallbackUpdate(3, 777, 100, BotCallbackKeys.FlowPersonalInviteExpiryManual, "cb-2"), CancellationToken.None);
        await processor.ProcessAsync(BuildMessageUpdate(4, 777, "24"), CancellationToken.None);

        Assert.Contains("Персональное приглашение создано", sender.ScreenMessages[^1].Text);
        Assert.Contains("Код:", sender.ScreenMessages[^1].Text);
    }

    [Fact]
    public async Task ProcessAsync_RemoveAdminFlow_RemovesSelectedAdminWithButtons()
    {
        var sender = new FakeTelegramMessageSender();
        await using var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await using var scope = provider.CreateAsyncScope();
        await InitializeAsync(scope);
        var processor = CreateProcessor(scope, sender);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        var service = await admin.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        await admin.RegisterOrUpdateUserAsync(new User { Id = 2001, FirstName = "Admin" }, CancellationToken.None);
        await admin.AddServiceAdminAsync(777, service.PublicId, 2001, CancellationToken.None);

        await processor.ProcessAsync(BuildCallbackUpdate(1, 777, 100, $"{BotCallbackKeys.FlowStartRemoveAdminPrefix}{service.PublicId}", "cb-1"), CancellationToken.None);
        await processor.ProcessAsync(BuildCallbackUpdate(2, 777, 100, $"{BotCallbackKeys.FlowRemoveAdminSelectPrefix}2001", "cb-2"), CancellationToken.None);

        var admins = await admin.GetServiceAdminsAsync(777, service.PublicId, CancellationToken.None);
        Assert.DoesNotContain(2001, admins);
    }

    [Fact]
    public async Task ProcessAsync_CreatorPermissionFlow_UpdatesPermission()
    {
        var sender = new FakeTelegramMessageSender();
        await using var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await using var scope = provider.CreateAsyncScope();
        await InitializeAsync(scope);
        var processor = CreateProcessor(scope, sender);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        await processor.ProcessAsync(BuildCallbackUpdate(1, 777, 100, BotCallbackKeys.FlowStartCreatorAllow, "cb-1"), CancellationToken.None);
        await processor.ProcessAsync(BuildMessageUpdate(2, 777, "3001"), CancellationToken.None);

        await admin.RegisterOrUpdateUserAsync(new User { Id = 3001, FirstName = "Creator" }, CancellationToken.None);
        var services = await admin.GetManagedServicesAsync(3001, CancellationToken.None);
        Assert.Empty(services);
        Assert.Contains("Доступ выдан", sender.ScreenMessages[^1].Text);
    }

    [Fact]
    public async Task ProcessAsync_InviteCodeDuringActiveFlow_IsHandledAsFlowPayload()
    {
        var sender = new FakeTelegramMessageSender();
        await using var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await using var scope = provider.CreateAsyncScope();
        await InitializeAsync(scope);
        var processor = CreateProcessor(scope, sender);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        var service = await admin.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await admin.CreateInviteAsync(777, service.PublicId, InviteCodeType.General, null, 1, 24, CancellationToken.None);

        await processor.ProcessAsync(BuildCallbackUpdate(1, 777, 100, BotCallbackKeys.FlowStartCreateService, "cb-1"), CancellationToken.None);
        await processor.ProcessAsync(BuildMessageUpdate(2, 777, invite.InviteCode), CancellationToken.None);

        Assert.Contains("Теперь введите описание сервиса", sender.ScreenMessages[^1].Text);
        var subscriptions = await admin.GetSubscriptionsAsync(777, CancellationToken.None);
        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ProcessAsync_UnsubscribeFlow_StillWorksWithButtons()
    {
        var sender = new FakeTelegramMessageSender();
        await using var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await using var scope = provider.CreateAsyncScope();
        await InitializeAsync(scope);
        var processor = CreateProcessor(scope, sender);
        var admin = scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>();

        var service = await admin.CreateServiceAsync(777, "Orders", "Order notifications", CancellationToken.None);
        var invite = await admin.CreateInviteAsync(777, service.PublicId, InviteCodeType.General, null, 1, 24, CancellationToken.None);
        await admin.RedeemInviteAsync(222, invite.InviteCode, CancellationToken.None);

        await processor.ProcessAsync(BuildCallbackUpdate(1, 222, 100, BotCallbackKeys.Subscriptions, "cb-1"), CancellationToken.None);
        await processor.ProcessAsync(BuildCallbackUpdate(2, 222, 100, $"{BotCallbackKeys.UnsubscribeAskPrefix}{service.PublicId}", "cb-2"), CancellationToken.None);
        await processor.ProcessAsync(BuildCallbackUpdate(3, 222, 100, $"{BotCallbackKeys.UnsubscribeYesPrefix}{service.PublicId}", "cb-3"), CancellationToken.None);

        var subscriptions = await admin.GetSubscriptionsAsync(222, CancellationToken.None);
        Assert.Empty(subscriptions);
    }

    [Fact]
    public async Task ProcessAsync_SlashCommand_ShowsButtonsOnlyNotice()
    {
        var sender = new FakeTelegramMessageSender();
        await using var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await using var scope = provider.CreateAsyncScope();
        await InitializeAsync(scope);
        var processor = CreateProcessor(scope, sender);

        await processor.ProcessAsync(BuildMessageUpdate(1, 222, "/services"), CancellationToken.None);

        Assert.Contains("Команды отключены", sender.ScreenMessages[^1].Text);
    }

    [Fact]
    public async Task ConversationState_IsPersistedOnUserEntity()
    {
        var sender = new FakeTelegramMessageSender();
        await using var provider = TestServiceFactory.Create(777, sender, new FakeTimeProvider(DateTimeOffset.UtcNow));
        await using var scope = provider.CreateAsyncScope();
        await InitializeAsync(scope);
        var processor = CreateProcessor(scope, sender);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await processor.ProcessAsync(BuildCallbackUpdate(1, 777, 100, BotCallbackKeys.FlowStartCreateService, "cb-1"), CancellationToken.None);

        var user = await db.Users.FindAsync(777L);
        Assert.Equal("CreateService", user!.ActiveBotFlowType);
        Assert.Equal("CreateServiceName", user.ActiveBotFlowStep);
        Assert.NotNull(user.ActiveBotFlowStartedAtUtc);
        Assert.NotNull(user.ActiveBotFlowContextJson);
    }

    private static async Task InitializeAsync(AsyncServiceScope scope)
    {
        var initializer = scope.ServiceProvider.GetRequiredService<StartupDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);
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

    private static TelegramUpdateProcessor CreateProcessor(AsyncServiceScope scope, FakeTelegramMessageSender sender) =>
        new(
            scope.ServiceProvider.GetRequiredService<IPlatformAdministrationService>(),
            scope.ServiceProvider.GetRequiredService<IUserContextService>(),
            scope.ServiceProvider.GetRequiredService<IBotConversationService>(),
            sender,
            NullLogger<TelegramUpdateProcessor>.Instance);
}
