using System.Net;
using Microsoft.EntityFrameworkCore;
using Notixa.Api.Contracts;
using Notixa.Api.Domain.Enums;
using Notixa.Api.Services;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Notixa.Api.Telegram;

public sealed class TelegramUpdateProcessor(
    IPlatformAdministrationService administrationService,
    IUserContextService userContextService,
    IBotConversationService conversationService,
    ITelegramMessageSender telegramMessageSender,
    ILogger<TelegramUpdateProcessor> logger) : ITelegramUpdateProcessor
{
    public async Task ProcessAsync(Update update, CancellationToken cancellationToken)
    {
        if (update.CallbackQuery is not null)
        {
            await ProcessCallbackQueryAsync(update.CallbackQuery, cancellationToken);
            return;
        }

        if (update.Message?.From is null || string.IsNullOrWhiteSpace(update.Message.Text))
        {
            return;
        }

        var from = update.Message.From;
        await administrationService.RegisterOrUpdateUserAsync(from, cancellationToken);

        try
        {
            var text = update.Message.Text.Trim();
            var isStartCommand = IsStartCommand(text);
            var screen = await HandleTextAsync(from.Id, text, cancellationToken);
            var preferredMessageId = isStartCommand
                ? null
                : await ResolvePreferredMessageIdAsync(from.Id, update.Message.Chat.Id, cancellationToken);
            await RenderScreenAsync(from.Id, update.Message.Chat.Id, screen, cancellationToken, preferredMessageId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to process telegram message from {TelegramUserId}.", from.Id);
            var body = ex is DbUpdateException
                ? "Не удалось сохранить изменения в базе. Попробуйте еще раз через несколько секунд."
                : Encode(ex.Message);
            var preferredMessageId = await ResolvePreferredMessageIdAsync(from.Id, update.Message.Chat.Id, cancellationToken);
            await RenderScreenAsync(
                from.Id,
                update.Message.Chat.Id,
                BuildStatusScreen("<b>⚠️ Ошибка</b>", body, await BuildHomeMarkupAsync(from.Id, cancellationToken)),
                cancellationToken,
                preferredMessageId);
        }
    }

    private async Task ProcessCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        if (callbackQuery.From is null || callbackQuery.Message?.Chat is null || string.IsNullOrWhiteSpace(callbackQuery.Data))
        {
            return;
        }

        await administrationService.RegisterOrUpdateUserAsync(callbackQuery.From, cancellationToken);

        try
        {
            var screen = await HandleCallbackAsync(callbackQuery.From.Id, callbackQuery.Data, cancellationToken);
            await RenderScreenAsync(
                callbackQuery.From.Id,
                callbackQuery.Message.Chat.Id,
                screen,
                cancellationToken,
                callbackQuery.Message.MessageId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to process telegram callback from {TelegramUserId}.", callbackQuery.From.Id);
            await RenderScreenAsync(
                callbackQuery.From.Id,
                callbackQuery.Message.Chat.Id,
                BuildStatusScreen("<b>⚠️ Ошибка</b>", "Не удалось обработать действие. Попробуйте еще раз через несколько секунд.", await BuildHomeMarkupAsync(callbackQuery.From.Id, cancellationToken)),
                cancellationToken,
                callbackQuery.Message.MessageId);
        }
    }

    private async Task<BotScreen> HandleTextAsync(long telegramUserId, string text, CancellationToken cancellationToken)
    {
        if (IsStartCommand(text))
        {
            await conversationService.ClearAsync(telegramUserId, cancellationToken);
            return await BuildHomeScreenAsync(telegramUserId, cancellationToken);
        }

        var flowScreen = await conversationService.HandleMessageAsync(telegramUserId, text, cancellationToken);
        if (flowScreen is not null)
        {
            return flowScreen;
        }

        if (text.StartsWith("/", StringComparison.Ordinal))
        {
            return BuildStatusScreen(
                "<b>ℹ️ Команды отключены</b>",
                "Этот бот больше работает через кнопки и подтверждения на экране. Выберите нужный раздел ниже.",
                await BuildHomeMarkupAsync(telegramUserId, cancellationToken));
        }

        return await HandleInvitePreviewAsync(telegramUserId, text, cancellationToken);
    }

    private async Task<BotScreen> HandleCallbackAsync(long telegramUserId, string data, CancellationToken cancellationToken)
    {
        var flowScreen = await conversationService.HandleCallbackAsync(telegramUserId, data, cancellationToken);
        if (flowScreen is not null)
        {
            return flowScreen;
        }

        if (data == BotCallbackKeys.Home) return await BuildHomeScreenAsync(telegramUserId, cancellationToken);
        if (data == BotCallbackKeys.Subscriptions) return await BuildSubscriptionsScreenAsync(telegramUserId, cancellationToken);
        if (data == BotCallbackKeys.Services) return await BuildServicesScreenAsync(telegramUserId, cancellationToken);
        if (data == BotCallbackKeys.CreatorManagement) return await BuildCreatorManagementScreenAsync(telegramUserId, cancellationToken);
        if (data.StartsWith(BotCallbackKeys.ServiceViewPrefix, StringComparison.Ordinal)) return await BuildServiceDetailsScreenAsync(telegramUserId, data[BotCallbackKeys.ServiceViewPrefix.Length..], cancellationToken);
        if (data.StartsWith(BotCallbackKeys.ServiceAdminsPrefix, StringComparison.Ordinal)) return await BuildServiceAdminsScreenAsync(telegramUserId, data[BotCallbackKeys.ServiceAdminsPrefix.Length..], cancellationToken);
        if (data.StartsWith(BotCallbackKeys.ServiceGeneralInvitePrefix, StringComparison.Ordinal)) return BuildInviteCreatedScreen(await administrationService.CreateInviteAsync(telegramUserId, data[BotCallbackKeys.ServiceGeneralInvitePrefix.Length..], InviteCodeType.General, null, null, null, cancellationToken), "✅ Приглашение создано");
        if (data.StartsWith(BotCallbackKeys.SubscribeYesPrefix, StringComparison.Ordinal)) return await HandleSubscribeConfirmationAsync(telegramUserId, data[BotCallbackKeys.SubscribeYesPrefix.Length..], cancellationToken);
        if (data.StartsWith(BotCallbackKeys.SubscribeNoPrefix, StringComparison.Ordinal)) return BuildStatusScreen("<b>👌 Подписка отменена</b>", "Когда будете готовы, просто отправьте код приглашения еще раз.", await BuildHomeMarkupAsync(telegramUserId, cancellationToken));
        if (data.StartsWith(BotCallbackKeys.UnsubscribeAskPrefix, StringComparison.Ordinal)) return await BuildUnsubscribeConfirmScreenAsync(telegramUserId, data[BotCallbackKeys.UnsubscribeAskPrefix.Length..], cancellationToken);
        if (data.StartsWith(BotCallbackKeys.UnsubscribeYesPrefix, StringComparison.Ordinal)) return await HandleUnsubscribeCallbackAsync(telegramUserId, data[BotCallbackKeys.UnsubscribeYesPrefix.Length..], cancellationToken);
        if (data.StartsWith(BotCallbackKeys.UnsubscribeNoPrefix, StringComparison.Ordinal)) return await BuildSubscriptionsScreenAsync(telegramUserId, cancellationToken);
        return await BuildHomeScreenAsync(telegramUserId, cancellationToken);
    }

    private async Task RenderScreenAsync(long telegramUserId, long chatId, BotScreen screen, CancellationToken cancellationToken, int? preferredMessageId = null)
    {
        var result = await telegramMessageSender.SendOrEditScreenAsync(chatId, preferredMessageId, screen.Message, screen.ParseMode, cancellationToken, screen.ReplyMarkup);
        if (result.MessageId > 0)
        {
            await administrationService.SaveBotScreenStateAsync(telegramUserId, result.ChatId, result.MessageId, cancellationToken);
        }
    }

    private async Task<int?> ResolvePreferredMessageIdAsync(long telegramUserId, long chatId, CancellationToken cancellationToken)
    {
        var state = await administrationService.GetBotScreenStateAsync(telegramUserId, cancellationToken);
        if (state.LastBotChatId == chatId && state.LastBotMessageId.HasValue)
        {
            return state.LastBotMessageId.Value;
        }

        return null;
    }

    private async Task<BotScreen> HandleInvitePreviewAsync(long telegramUserId, string text, CancellationToken cancellationToken)
    {
        var preview = await administrationService.PreviewInviteAsync(telegramUserId, text, cancellationToken);
        return preview.Status switch
        {
            PreviewInviteStatus.Invalid => BuildStatusScreen("<b>⚠️ Код не принят</b>", "Код приглашения недействителен, истек или уже исчерпан.", await BuildHomeMarkupAsync(telegramUserId, cancellationToken)),
            PreviewInviteStatus.AlreadySubscribed => new BotScreen(
                $"""
                <b>ℹ️ Вы уже подписаны на этот сервис</b>
                Сервис: <b>{Encode(preview.ServiceName!)}</b> <code>{Encode(preview.ServicePublicId!)}</code>
                """,
                TemplateParseMode.Html,
                BuildUnsubscribeMarkup(preview.ServicePublicId!, preview.ServiceName!)),
            _ => new BotScreen(
                $"""
                <b>🔔 Подписка на сервис</b>
                Вы хотите подписаться на уведомления от сервиса <b>{Encode(preview.ServiceName!)}</b>?
                """,
                TemplateParseMode.Html,
                BuildSubscribeConfirmationKeyboard(text))
        };
    }

    private async Task<BotScreen> HandleSubscribeConfirmationAsync(long telegramUserId, string inviteCode, CancellationToken cancellationToken)
    {
        var redeemResult = await administrationService.RedeemInviteAsync(telegramUserId, inviteCode, cancellationToken);
        return redeemResult.Status switch
        {
            RedeemInviteStatus.Created => new BotScreen(
                $"""
                <b>✅ Подписка подключена</b>
                Сервис: <b>{Encode(redeemResult.Subscription!.ServiceName)}</b> <code>{Encode(redeemResult.Subscription.ServicePublicId)}</code>
                """,
                TemplateParseMode.Html,
                BuildUnsubscribeMarkup(redeemResult.Subscription.ServicePublicId, redeemResult.Subscription.ServiceName)),
            RedeemInviteStatus.AlreadySubscribed => new BotScreen(
                $"""
                <b>ℹ️ Вы уже подписаны</b>
                Сервис: <b>{Encode(redeemResult.Subscription!.ServiceName)}</b> <code>{Encode(redeemResult.Subscription.ServicePublicId)}</code>
                """,
                TemplateParseMode.Html,
                BuildUnsubscribeMarkup(redeemResult.Subscription.ServicePublicId, redeemResult.Subscription.ServiceName)),
            _ => BuildStatusScreen("<b>⚠️ Подписка не выполнена</b>", "Приглашение недействительно, уже истекло или исчерпано.", await BuildHomeMarkupAsync(telegramUserId, cancellationToken))
        };
    }

    private async Task<BotScreen> HandleUnsubscribeCallbackAsync(long telegramUserId, string servicePublicId, CancellationToken cancellationToken)
    {
        var removed = await administrationService.UnsubscribeAsync(telegramUserId, servicePublicId, cancellationToken);
        return BuildStatusScreen(
            removed ? "<b>✅ Подписка отключена</b>" : "<b>⚠️ Отписка не выполнена</b>",
            removed ? $"Сервис: <code>{Encode(servicePublicId)}</code>" : $"Активная подписка для сервиса <code>{Encode(servicePublicId)}</code> не найдена.",
            await BuildSubscriptionsMarkupAsync(telegramUserId, cancellationToken));
    }

    private async Task<BotScreen> BuildHomeScreenAsync(long telegramUserId, CancellationToken cancellationToken) =>
        new(
            """
            <b>🔔 Notixa</b>
            Управление ботом полностью перенесено в кнопки и экранные сценарии.

            Отправьте код приглашения, чтобы подписаться, или выберите раздел ниже.
            """,
            TemplateParseMode.Html,
            await BuildHomeMarkupAsync(telegramUserId, cancellationToken));

    private async Task<BotScreen> BuildSubscriptionsScreenAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var subscriptions = await administrationService.GetSubscriptionsAsync(telegramUserId, cancellationToken);
        if (subscriptions.Count == 0)
        {
            return new BotScreen(
                "<b>📬 Мои подписки</b>\nУ вас пока нет активных подписок.\n\nОтправьте код приглашения, чтобы подключить сервис.",
                TemplateParseMode.Html,
                await BuildSubscriptionsMarkupAsync(telegramUserId, cancellationToken));
        }

        var lines = subscriptions.Select(x => $"• <b>{Encode(x.ServiceName)}</b> <code>{Encode(x.ServicePublicId)}</code>");
        return new BotScreen(
            $"<b>📬 Мои подписки</b>\n{string.Join($"{Environment.NewLine}{Environment.NewLine}", lines)}",
            TemplateParseMode.Html,
            await BuildSubscriptionsMarkupAsync(telegramUserId, cancellationToken, subscriptions));
    }

    private async Task<BotScreen> BuildServicesScreenAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var services = await administrationService.GetManagedServicesAsync(telegramUserId, cancellationToken);
        if (services.Count == 0)
        {
            return new BotScreen(
                "<b>🛠️ Мои сервисы</b>\nУ вас пока нет сервисов или прав администратора.",
                TemplateParseMode.Html,
                await BuildServicesMarkupAsync(telegramUserId, cancellationToken));
        }

        var lines = services.Select(x => $"• <b>{Encode(x.Name)}</b> <code>{Encode(x.PublicId)}</code>\n{Encode(x.Description)}");
        return new BotScreen(
            $"<b>🛠️ Мои сервисы</b>\n{string.Join($"{Environment.NewLine}{Environment.NewLine}", lines)}",
            TemplateParseMode.Html,
            await BuildServicesMarkupAsync(telegramUserId, cancellationToken, services));
    }

    private async Task<BotScreen> BuildServiceDetailsScreenAsync(long telegramUserId, string serviceId, CancellationToken cancellationToken)
    {
        var service = (await administrationService.GetManagedServicesAsync(telegramUserId, cancellationToken)).SingleOrDefault(x => x.PublicId == serviceId)
            ?? throw new InvalidOperationException("Сервис не найден.");
        return new BotScreen(
            $"<b>⚙️ {Encode(service.Name)}</b>\n<code>{Encode(service.PublicId)}</code>\n\n{Encode(service.Description)}",
            TemplateParseMode.Html,
            BuildServiceActionsMarkup(service.PublicId));
    }

    private async Task<BotScreen> BuildServiceAdminsScreenAsync(long telegramUserId, string serviceId, CancellationToken cancellationToken)
    {
        var admins = await administrationService.GetServiceAdminsAsync(telegramUserId, serviceId, cancellationToken);
        var body = admins.Count == 0 ? "Администраторы пока не назначены." : string.Join(Environment.NewLine, admins.Select(x => $"• <code>{x}</code>"));
        return new BotScreen(
            $"<b>👥 Администраторы сервиса</b>\n{body}",
            TemplateParseMode.Html,
            new InlineKeyboardMarkup(
            [
                [InlineKeyboardButton.WithCallbackData("➕ Добавить админа", $"{BotCallbackKeys.FlowStartAddAdminPrefix}{serviceId}"), InlineKeyboardButton.WithCallbackData("➖ Удалить админа", $"{BotCallbackKeys.FlowStartRemoveAdminPrefix}{serviceId}")],
                [InlineKeyboardButton.WithCallbackData("⬅️ К сервису", $"{BotCallbackKeys.ServiceViewPrefix}{serviceId}"), InlineKeyboardButton.WithCallbackData("🏠 Главное меню", BotCallbackKeys.Home)]
            ]));
    }

    private Task<BotScreen> BuildCreatorManagementScreenAsync(long telegramUserId, CancellationToken cancellationToken) =>
        Task.FromResult(
            new BotScreen(
                "<b>👑 Управление создателями</b>\nВыберите действие и затем укажите Telegram user id.",
                TemplateParseMode.Html,
                new InlineKeyboardMarkup(
                [
                    [InlineKeyboardButton.WithCallbackData("✅ Разрешить создание", BotCallbackKeys.FlowStartCreatorAllow), InlineKeyboardButton.WithCallbackData("⛔ Запретить создание", BotCallbackKeys.FlowStartCreatorDeny)],
                    [InlineKeyboardButton.WithCallbackData("🏠 Главное меню", BotCallbackKeys.Home)]
                ])));

    private async Task<BotScreen> BuildUnsubscribeConfirmScreenAsync(long telegramUserId, string servicePublicId, CancellationToken cancellationToken)
    {
        var subscription = (await administrationService.GetSubscriptionsAsync(telegramUserId, cancellationToken))
            .SingleOrDefault(x => x.ServicePublicId == servicePublicId);

        if (subscription is null)
        {
            return BuildStatusScreen("<b>⚠️ Отписка не выполнена</b>", $"Активная подписка для сервиса <code>{Encode(servicePublicId)}</code> не найдена.", await BuildSubscriptionsMarkupAsync(telegramUserId, cancellationToken));
        }

        return new BotScreen(
            $"""
            <b>⚠️ Подтвердите отписку</b>
            Вы хотите отписаться от сервиса <b>{Encode(subscription.ServiceName)}</b>?
            """,
            TemplateParseMode.Html,
            BuildUnsubscribeConfirmationMarkup(servicePublicId));
    }

    private BotScreen BuildInviteCreatedScreen(CreateInviteResult result, string title)
    {
        return new BotScreen(
            $"""
            <b>{title}</b>
            Сервис: <code>{Encode(result.ServicePublicId)}</code>
            Код: <code>{Encode(result.InviteCode)}</code>
            """,
            TemplateParseMode.Html,
            BuildServiceNavigationMarkup(result.ServicePublicId));
    }

    private static BotScreen BuildStatusScreen(string title, string body, ReplyMarkup? replyMarkup) =>
        new($"{title}\n{body}", TemplateParseMode.Html, replyMarkup);

    private async Task<InlineKeyboardMarkup> BuildHomeMarkupAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var context = await userContextService.GetUserContextAsync(telegramUserId, cancellationToken);
        var rows = new List<InlineKeyboardButton[]>
        {
            new[] { InlineKeyboardButton.WithCallbackData("📬 Мои подписки", BotCallbackKeys.Subscriptions) }
        };

        if (context.CanCreateServices)
        {
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🛠️ Мои сервисы", BotCallbackKeys.Services) });
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("➕ Создать сервис", BotCallbackKeys.FlowStartCreateService) });
        }

        if (context.IsSuperAdmin)
        {
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("👑 Создатели", BotCallbackKeys.CreatorManagement) });
        }

        return new InlineKeyboardMarkup(rows);
    }

    private async Task<InlineKeyboardMarkup> BuildSubscriptionsMarkupAsync(long telegramUserId, CancellationToken cancellationToken, IReadOnlyCollection<SubscriptionListItem>? subscriptions = null)
    {
        subscriptions ??= await administrationService.GetSubscriptionsAsync(telegramUserId, cancellationToken);
        var rows = subscriptions.Select(x => new[] { InlineKeyboardButton.WithCallbackData($"🗑️ Отписаться от {ShortLabel(x.ServiceName)}", $"{BotCallbackKeys.UnsubscribeAskPrefix}{x.ServicePublicId}") }).ToList();
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", BotCallbackKeys.Home) });
        return new InlineKeyboardMarkup(rows);
    }

    private async Task<InlineKeyboardMarkup> BuildServicesMarkupAsync(long telegramUserId, CancellationToken cancellationToken, IReadOnlyCollection<ServiceListItem>? services = null)
    {
        services ??= await administrationService.GetManagedServicesAsync(telegramUserId, cancellationToken);
        var rows = services.Select(x => new[] { InlineKeyboardButton.WithCallbackData($"⚙️ {ShortLabel(x.Name)}", $"{BotCallbackKeys.ServiceViewPrefix}{x.PublicId}") }).ToList();
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("➕ Создать сервис", BotCallbackKeys.FlowStartCreateService) });
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", BotCallbackKeys.Home) });
        return new InlineKeyboardMarkup(rows);
    }

    private static InlineKeyboardMarkup BuildServiceActionsMarkup(string serviceId) => new(
    [
        [InlineKeyboardButton.WithCallbackData("🧩 Создать шаблон", $"{BotCallbackKeys.FlowStartCreateTemplatePrefix}{serviceId}"), InlineKeyboardButton.WithCallbackData("🎟️ Общее приглашение", $"{BotCallbackKeys.ServiceGeneralInvitePrefix}{serviceId}")],
        [InlineKeyboardButton.WithCallbackData("👤 Персональное приглашение", $"{BotCallbackKeys.FlowStartPersonalInvitePrefix}{serviceId}"), InlineKeyboardButton.WithCallbackData("👥 Показать админов", $"{BotCallbackKeys.ServiceAdminsPrefix}{serviceId}")],
        [InlineKeyboardButton.WithCallbackData("➕ Добавить админа", $"{BotCallbackKeys.FlowStartAddAdminPrefix}{serviceId}"), InlineKeyboardButton.WithCallbackData("➖ Удалить админа", $"{BotCallbackKeys.FlowStartRemoveAdminPrefix}{serviceId}")],
        [InlineKeyboardButton.WithCallbackData("⬅️ К сервисам", BotCallbackKeys.Services), InlineKeyboardButton.WithCallbackData("🏠 Главное меню", BotCallbackKeys.Home)]
    ]);

    private static InlineKeyboardMarkup BuildServiceNavigationMarkup(string serviceId) => new(
    [
        [InlineKeyboardButton.WithCallbackData("⬅️ К сервису", $"{BotCallbackKeys.ServiceViewPrefix}{serviceId}"), InlineKeyboardButton.WithCallbackData("🏠 Главное меню", BotCallbackKeys.Home)]
    ]);

    private static InlineKeyboardMarkup BuildSubscribeConfirmationKeyboard(string inviteCode) => new(
    [
        [InlineKeyboardButton.WithCallbackData("✅ Да", $"{BotCallbackKeys.SubscribeYesPrefix}{inviteCode}"), InlineKeyboardButton.WithCallbackData("❌ Нет", $"{BotCallbackKeys.SubscribeNoPrefix}{inviteCode}")],
        [InlineKeyboardButton.WithCallbackData("🏠 Главное меню", BotCallbackKeys.Home)]
    ]);

    private static InlineKeyboardMarkup BuildUnsubscribeMarkup(string serviceId, string serviceName) => new(
    [
        [InlineKeyboardButton.WithCallbackData($"🗑️ Отписаться от {ShortLabel(serviceName)}", $"{BotCallbackKeys.UnsubscribeAskPrefix}{serviceId}")],
        [InlineKeyboardButton.WithCallbackData("📬 Мои подписки", BotCallbackKeys.Subscriptions), InlineKeyboardButton.WithCallbackData("🏠 Главное меню", BotCallbackKeys.Home)]
    ]);

    private static InlineKeyboardMarkup BuildUnsubscribeConfirmationMarkup(string serviceId) => new(
    [
        [InlineKeyboardButton.WithCallbackData("✅ Да", $"{BotCallbackKeys.UnsubscribeYesPrefix}{serviceId}"), InlineKeyboardButton.WithCallbackData("❌ Нет", $"{BotCallbackKeys.UnsubscribeNoPrefix}{serviceId}")]
    ]);

    private static bool IsStartCommand(string text)
    {
        if (text.Equals("Start", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!text.StartsWith("/start", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Length == "/start".Length)
        {
            return true;
        }

        var next = text["/start".Length];
        return next == ' ' || next == '@';
    }

    private static string ShortLabel(string text) => text.Length <= 24 ? text : $"{text[..21]}...";

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
