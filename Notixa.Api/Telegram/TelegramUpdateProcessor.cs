using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Notixa.Api.Contracts;
using Notixa.Api.Domain.Enums;
using Notixa.Api.Options;
using Notixa.Api.Services;
using System.Net;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Notixa.Api.Telegram;

public sealed class TelegramUpdateProcessor(
    IPlatformAdministrationService administrationService,
    IUserContextService userContextService,
    IOptions<TelegramBotOptions> telegramBotOptions,
    IBotClientAccessor botClientAccessor,
    ITelegramMessageSender telegramMessageSender,
    ILogger<TelegramUpdateProcessor> logger) : ITelegramUpdateProcessor
{
    private const string SubscribeYesPrefix = "sub:yes:";
    private const string SubscribeNoPrefix = "sub:no:";
    private const string UnsubscribePrefix = "unsub:";

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
        var chatId = update.Message.Chat.Id;
        var text = update.Message.Text.Trim();

        await administrationService.RegisterOrUpdateUserAsync(from, cancellationToken);

        try
        {
            var result = await HandleTextAsync(from.Id, text, cancellationToken);
            foreach (var reply in result.Replies)
            {
                await telegramMessageSender.SendAsync(chatId, reply.Message, reply.ParseMode, cancellationToken, reply.ReplyMarkup);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to process telegram message from {TelegramUserId}.", from.Id);
            var menu = await BuildMainMenuAsync(from.Id, cancellationToken);
            var userMessage = ex is DbUpdateException
                ? "Не удалось сохранить изменения в базе. Попробуйте еще раз через несколько секунд."
                : ex.Message;
            await telegramMessageSender.SendAsync(
                chatId,
                $"<b>⚠️ Ошибка</b>\n{Encode(userMessage)}",
                TemplateParseMode.Html,
                cancellationToken,
                menu);
        }
    }

    private async Task<PlatformCommandResult> HandleTextAsync(long telegramUserId, string text, CancellationToken cancellationToken)
    {
        var menu = await BuildMainMenuAsync(telegramUserId, cancellationToken);

        if (IsHelpCommand(text))
        {
            return PlatformCommandResult.Single(await BuildHelpTextAsync(telegramUserId, cancellationToken), TemplateParseMode.Html, menu);
        }

        if (IsSubscriptionsCommand(text))
        {
            var subscriptions = await administrationService.GetSubscriptionsAsync(telegramUserId, cancellationToken);
            if (subscriptions.Count == 0)
            {
                return PlatformCommandResult.Single(
                    "<b>📬 Мои подписки</b>\nУ вас пока нет активных подписок.\n\nОтправьте код приглашения, чтобы подключить сервис.",
                    TemplateParseMode.Html,
                    menu);
            }

            var lines = subscriptions.Select(x =>
                $"""
                • <b>{Encode(x.ServiceName)}</b> <code>{Encode(x.ServicePublicId)}</code>
                Команда для отписки:
                <code>/unsubscribe {Encode(x.ServicePublicId)}</code>
                """);
            return PlatformCommandResult.Single(
                $"<b>📬 Мои подписки</b>\n{string.Join($"{Environment.NewLine}{Environment.NewLine}", lines)}",
                TemplateParseMode.Html,
                BuildSubscriptionsMarkup(subscriptions));
        }

        if (IsUnsubscribeHelpCommand(text))
        {
            return PlatformCommandResult.Single("""
                <b>➖ Как отписаться</b>
                Откройте раздел <b>📬 Мои подписки</b> и скопируйте готовую команду под нужным сервисом.

                Формат команды:
                <code>/unsubscribe serviceId</code>

                Пример:
                <code>/unsubscribe 720b6400c2</code>
                """, TemplateParseMode.Html, menu);
        }

        if (IsServicesCommand(text))
        {
            var services = await administrationService.GetManagedServicesAsync(telegramUserId, cancellationToken);
            if (services.Count == 0)
            {
                return PlatformCommandResult.Single(
                    "<b>🛠️ Мои сервисы</b>\nУ вас пока нет сервисов или прав администратора.",
                    TemplateParseMode.Html,
                    menu);
            }

            var lines = services.Select(x =>
                $"• <code>{Encode(x.PublicId)}</code> | <b>{Encode(x.Name)}</b> | {Encode(x.Description)}");
            return PlatformCommandResult.Single($"<b>🛠️ Мои сервисы</b>\n{string.Join(Environment.NewLine, lines)}", TemplateParseMode.Html, menu);
        }

        if (IsCreateServiceHelpCommand(text))
        {
            return PlatformCommandResult.Single("""
                <b>➕ Как создать сервис</b>
                Отправьте команду:
                <code>/create_service Название | Описание</code>

                Пример:
                <code>/create_service Orders | Уведомления по заказам</code>
                """, TemplateParseMode.Html, menu);
        }

        if (IsTemplateHelpCommand(text))
        {
            return PlatformCommandResult.Single("""
                <b>🧩 Как создать шаблон</b>
                Отправьте команду:
                <code>/create_template serviceId templateKey PlainText | Текст {{переменная}}</code>

                Пример:
                <code>/create_template abc123 hello PlainText | Привет, {{name}}</code>
                """, TemplateParseMode.Html, menu);
        }

        if (IsInviteHelpCommand(text))
        {
            return PlatformCommandResult.Single("""
                <b>🎟️ Как создать приглашение</b>
                Общее приглашение:
                <code>/generate_general_invite serviceId</code>

                Персональное приглашение:
                <code>/generate_personal_invite serviceId externalUserKey</code>
                """, TemplateParseMode.Html, menu);
        }

        if (IsCreatorManagementCommand(text))
        {
            return PlatformCommandResult.Single("""
                <b>👑 Управление создателями сервисов</b>
                Выдать доступ:
                <code>/allow_creator telegramUserId</code>

                Забрать доступ:
                <code>/deny_creator telegramUserId</code>
                """, TemplateParseMode.Html, menu);
        }

        if (text.StartsWith("/allow_creator ", StringComparison.OrdinalIgnoreCase))
        {
            var userId = ParseLongArgument(text);
            var success = await administrationService.SetCreatorPermissionAsync(telegramUserId, userId, true, cancellationToken);
            return PlatformCommandResult.Single(
                success
                    ? $"<b>✅ Доступ выдан</b>\nПользователь <code>{userId}</code> теперь может создавать сервисы."
                    : "<b>⛔ Доступ запрещен</b>\nТолько супер-админ может управлять создателями сервисов.",
                TemplateParseMode.Html,
                menu);
        }

        if (text.StartsWith("/deny_creator ", StringComparison.OrdinalIgnoreCase))
        {
            var userId = ParseLongArgument(text);
            var success = await administrationService.SetCreatorPermissionAsync(telegramUserId, userId, false, cancellationToken);
            return PlatformCommandResult.Single(
                success
                    ? $"<b>✅ Доступ отозван</b>\nПользователь <code>{userId}</code> больше не может создавать сервисы."
                    : "<b>⛔ Доступ запрещен</b>\nТолько супер-админ может управлять создателями сервисов.",
                TemplateParseMode.Html,
                menu);
        }

        if (text.StartsWith("/create_service ", StringComparison.OrdinalIgnoreCase))
        {
            var payload = text["/create_service ".Length..];
            var parts = payload.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                throw new InvalidOperationException("Использование: /create_service Название | Описание");
            }

            var result = await administrationService.CreateServiceAsync(telegramUserId, parts[0], parts[1], cancellationToken);
            return PlatformCommandResult.Single($"""
                <b>✅ Сервис создан</b>
                Идентификатор сервиса: <code>{Encode(result.PublicId)}</code>
                Ключ сервиса: <code>{Encode(result.ServiceKey)}</code>

                <b>⚠️ Сохраните ключ сервиса прямо сейчас.</b>
                <b>Позже получить этот ключ повторно будет нельзя.</b>
                """, TemplateParseMode.Html, menu);
        }

        if (text.StartsWith("/create_template ", StringComparison.OrdinalIgnoreCase))
        {
            var payload = text["/create_template ".Length..];
            var parts = payload.Split('|', 2, StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                throw new InvalidOperationException("Использование: /create_template serviceId templateKey parseMode | текст шаблона");
            }

            var head = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (head.Length < 3)
            {
                throw new InvalidOperationException("Использование: /create_template serviceId templateKey parseMode | текст шаблона");
            }

            var parseMode = Enum.Parse<TemplateParseMode>(head[2], true);
            var result = await administrationService.UpsertTemplateAsync(telegramUserId, head[0], head[1], parseMode, parts[1], cancellationToken);
            return PlatformCommandResult.Single(
                $"<b>✅ Шаблон сохранен</b>\nСервис: <code>{Encode(result.ServicePublicId)}</code>\nШаблон: <code>{Encode(result.TemplateKey)}</code>",
                TemplateParseMode.Html,
                menu);
        }

        if (text.StartsWith("/generate_general_invite ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                throw new InvalidOperationException("Использование: /generate_general_invite serviceId [usageLimit] [expiresHours]");
            }

            if (parts.Length >= 3 && !int.TryParse(parts[2], out _))
            {
                throw new InvalidOperationException("""
                    Для общего приглашения второй параметр должен быть числом.
                    Если вы хотите указать внешний ключ пользователя, используйте:
                    /generate_personal_invite serviceId externalUserKey [expiresHours]
                    """);
            }

            if (parts.Length >= 4 && !int.TryParse(parts[3], out _))
            {
                throw new InvalidOperationException("Параметр expiresHours должен быть числом.");
            }

            int? usageLimit = parts.Length >= 3 ? int.Parse(parts[2]) : null;
            int? expiresHours = parts.Length >= 4 ? int.Parse(parts[3]) : null;
            var result = await administrationService.CreateInviteAsync(telegramUserId, parts[1], InviteCodeType.General, null, usageLimit, expiresHours, cancellationToken);
            var inviteLink = BuildInviteLink(result.InviteCode);
            return PlatformCommandResult.Single(
                $"""
                <b>✅ Приглашение создано</b>
                Сервис: <code>{Encode(result.ServicePublicId)}</code>
                Код: <code>{Encode(result.InviteCode)}</code>
                Ссылка: {BuildLinkMarkup(inviteLink)}
                """,
                TemplateParseMode.Html,
                menu);
        }

        if (text.StartsWith("/generate_personal_invite ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                throw new InvalidOperationException("Использование: /generate_personal_invite serviceId externalUserKey [expiresHours]");
            }

            if (parts.Length >= 4 && !int.TryParse(parts[3], out _))
            {
                throw new InvalidOperationException("Параметр expiresHours должен быть числом.");
            }

            int? expiresHours = parts.Length >= 4 ? int.Parse(parts[3]) : null;
            var result = await administrationService.CreateInviteAsync(telegramUserId, parts[1], InviteCodeType.Personal, parts[2], 1, expiresHours, cancellationToken);
            var inviteLink = BuildInviteLink(result.InviteCode);
            return PlatformCommandResult.Single(
                $"""
                <b>✅ Персональное приглашение создано</b>
                Сервис: <code>{Encode(result.ServicePublicId)}</code>
                Код: <code>{Encode(result.InviteCode)}</code>
                Ссылка: {BuildLinkMarkup(inviteLink)}
                """,
                TemplateParseMode.Html,
                menu);
        }

        if (text.StartsWith("/service_admins ", StringComparison.OrdinalIgnoreCase))
        {
            var serviceId = text["/service_admins ".Length..].Trim();
            var admins = await administrationService.GetServiceAdminsAsync(telegramUserId, serviceId, cancellationToken);
            return PlatformCommandResult.Single(
                admins.Count == 0
                    ? "<b>👥 Администраторы сервиса</b>\nАдминистраторы пока не назначены."
                    : $"<b>👥 Администраторы сервиса</b>\n{string.Join(Environment.NewLine, admins.Select(x => $"• <code>{x}</code>"))}",
                TemplateParseMode.Html,
                menu);
        }

        if (text.StartsWith("/add_service_admin ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                throw new InvalidOperationException("Usage: /add_service_admin serviceId telegramUserId");
            }

            await administrationService.AddServiceAdminAsync(telegramUserId, parts[1], long.Parse(parts[2]), cancellationToken);
            return PlatformCommandResult.Single("<b>✅ Администратор сервиса добавлен</b>", TemplateParseMode.Html, menu);
        }

        if (text.StartsWith("/remove_service_admin ", StringComparison.OrdinalIgnoreCase))
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
            {
                throw new InvalidOperationException("Usage: /remove_service_admin serviceId telegramUserId");
            }

            await administrationService.RemoveServiceAdminAsync(telegramUserId, parts[1], long.Parse(parts[2]), cancellationToken);
            return PlatformCommandResult.Single("<b>✅ Администратор сервиса удален</b>", TemplateParseMode.Html, menu);
        }

        if (text.StartsWith("/unsubscribe ", StringComparison.OrdinalIgnoreCase))
        {
            var serviceId = text["/unsubscribe ".Length..].Trim();
            var removed = await administrationService.UnsubscribeAsync(telegramUserId, serviceId, cancellationToken);
            return PlatformCommandResult.Single(
                removed
                    ? $"<b>✅ Подписка отключена</b>\nСервис: <code>{Encode(serviceId)}</code>"
                    : $"<b>⚠️ Отписка не выполнена</b>\nАктивная подписка для сервиса <code>{Encode(serviceId)}</code> не найдена.",
                TemplateParseMode.Html,
                menu);
        }

        var preview = await administrationService.PreviewInviteAsync(telegramUserId, text, cancellationToken);
        return preview.Status switch
        {
            PreviewInviteStatus.Invalid => PlatformCommandResult.Single(
                "<b>⚠️ Код не принят</b>\nКод приглашения недействителен, истек или уже исчерпан.",
                TemplateParseMode.Html,
                menu),
            PreviewInviteStatus.AlreadySubscribed => PlatformCommandResult.Single(
                $"""
                <b>ℹ️ Вы уже подписаны на этот сервис</b>
                Сервис: <b>{Encode(preview.ServiceName!)}</b> <code>{Encode(preview.ServicePublicId!)}</code>
                Команда для отписки:
                <code>/unsubscribe {Encode(preview.ServicePublicId!)}</code>
                """,
                TemplateParseMode.Html,
                BuildUnsubscribeMarkup(preview.ServicePublicId!)),
            _ => PlatformCommandResult.Single(
                $"""
                <b>🔔 Подписка на сервис</b>
                Вы хотите подписаться на уведомления от сервиса <b>{Encode(preview.ServiceName!)}</b>?

                Идентификатор сервиса: <code>{Encode(preview.ServicePublicId!)}</code>
                """,
                TemplateParseMode.Html,
                BuildSubscribeConfirmationKeyboard(text))
        };
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
            var menu = await BuildMainMenuAsync(callbackQuery.From.Id, cancellationToken);
            string message;

            if (callbackQuery.Data.StartsWith(SubscribeYesPrefix, StringComparison.Ordinal))
            {
                var inviteCode = callbackQuery.Data[SubscribeYesPrefix.Length..];
                var redeemResult = await administrationService.RedeemInviteAsync(callbackQuery.From.Id, inviteCode, cancellationToken);
                message = redeemResult.Status switch
                {
                    RedeemInviteStatus.Created => $"<b>✅ Подписка подключена</b>\nСервис: <b>{Encode(redeemResult.Subscription!.ServiceName)}</b> <code>{Encode(redeemResult.Subscription.ServicePublicId)}</code>\nКоманда для отписки:\n<code>/unsubscribe {Encode(redeemResult.Subscription.ServicePublicId)}</code>",
                    RedeemInviteStatus.AlreadySubscribed => $"<b>ℹ️ Вы уже подписаны</b>\nСервис: <b>{Encode(redeemResult.Subscription!.ServiceName)}</b> <code>{Encode(redeemResult.Subscription.ServicePublicId)}</code>\nКоманда для отписки:\n<code>/unsubscribe {Encode(redeemResult.Subscription.ServicePublicId)}</code>",
                    _ => "<b>⚠️ Подписка не выполнена</b>\nПриглашение недействительно, уже истекло или исчерпано."
                };

                ReplyMarkup replyMarkup = redeemResult.Status switch
                {
                    RedeemInviteStatus.Created => BuildUnsubscribeMarkup(redeemResult.Subscription!.ServicePublicId),
                    RedeemInviteStatus.AlreadySubscribed => BuildUnsubscribeMarkup(redeemResult.Subscription!.ServicePublicId),
                    _ => menu
                };

                await telegramMessageSender.SendAsync(
                    callbackQuery.Message.Chat.Id,
                    message,
                    TemplateParseMode.Html,
                    cancellationToken,
                    replyMarkup);

                await AnswerCallbackAsync(callbackQuery.Id, null, cancellationToken);
                return;
            }
            else if (callbackQuery.Data.StartsWith(UnsubscribePrefix, StringComparison.Ordinal))
            {
                var serviceId = callbackQuery.Data[UnsubscribePrefix.Length..];
                var removed = await administrationService.UnsubscribeAsync(callbackQuery.From.Id, serviceId, cancellationToken);
                message = removed
                    ? $"<b>✅ Подписка отключена</b>\nСервис: <code>{Encode(serviceId)}</code>"
                    : $"<b>⚠️ Отписка не выполнена</b>\nАктивная подписка для сервиса <code>{Encode(serviceId)}</code> не найдена.";

                await telegramMessageSender.SendAsync(
                    callbackQuery.Message.Chat.Id,
                    message,
                    TemplateParseMode.Html,
                    cancellationToken,
                    menu);

                await AnswerCallbackAsync(
                    callbackQuery.Id,
                    removed ? "Подписка отключена." : "Подписка не найдена.",
                    cancellationToken);
                return;
            }
            else if (callbackQuery.Data.StartsWith(SubscribeNoPrefix, StringComparison.Ordinal))
            {
                message = "<b>👌 Подписка отменена</b>\nКогда будете готовы, просто отправьте код приглашения еще раз.";
            }
            else
            {
                return;
            }

            await telegramMessageSender.SendAsync(
                callbackQuery.Message.Chat.Id,
                message,
                TemplateParseMode.Html,
                cancellationToken,
                menu);

            await AnswerCallbackAsync(callbackQuery.Id, null, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to process telegram callback from {TelegramUserId}.", callbackQuery.From.Id);
            var menu = await BuildMainMenuAsync(callbackQuery.From.Id, cancellationToken);
            await telegramMessageSender.SendAsync(
                callbackQuery.Message.Chat.Id,
                "<b>⚠️ Ошибка</b>\nНе удалось обработать действие. Попробуйте еще раз через несколько секунд.",
                TemplateParseMode.Html,
                cancellationToken,
                menu);
            await AnswerCallbackAsync(callbackQuery.Id, "Не удалось обработать действие.", cancellationToken);
        }
    }

    private async Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken cancellationToken)
    {
        if (!botClientAccessor.IsConfigured)
        {
            return;
        }

        await botClientAccessor.Client!.AnswerCallbackQuery(
            callbackQueryId,
            text: text,
            cancellationToken: cancellationToken);
    }

    private static long ParseLongArgument(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !long.TryParse(parts[1], out var userId))
        {
            throw new InvalidOperationException("Expected a single numeric Telegram user id.");
        }

        return userId;
    }

    private async Task<string> BuildHelpTextAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var context = await userContextService.GetUserContextAsync(telegramUserId, cancellationToken);
        var services = await administrationService.GetManagedServicesAsync(telegramUserId, cancellationToken);

        var parts = new List<string>
        {
            "<b>👋 Добро пожаловать в Notixa</b>",
            "Чтобы подключиться к сервису, просто пришлите боту <b>код приглашения</b>.",
            "Ниже доступны быстрые кнопки для подписок и управления сервисами."
        };

        if (services.Count > 0 || context.CanCreateServices)
        {
            parts.Add("""

                <b>🛠️ Работа с сервисами</b>
                <code>/create_service Название | Описание</code>
                <code>/create_template serviceId templateKey PlainText | Текст {{name}}</code>
                <code>/generate_general_invite serviceId</code>
                <code>/generate_personal_invite serviceId externalUserKey</code>
                <code>/service_admins serviceId</code>
                <code>/add_service_admin serviceId telegramUserId</code>
                <code>/remove_service_admin serviceId telegramUserId</code>
                """);
        }

        if (context.IsSuperAdmin)
        {
            parts.Add("""

                <b>👑 Управление создателями сервисов</b>
                <code>/allow_creator telegramUserId</code>
                <code>/deny_creator telegramUserId</code>
                """);
        }

        return string.Join(Environment.NewLine, parts);
    }

    private async Task<ReplyKeyboardMarkup> BuildMainMenuAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var context = await userContextService.GetUserContextAsync(telegramUserId, cancellationToken);
        var services = await administrationService.GetManagedServicesAsync(telegramUserId, cancellationToken);
        var hasServiceAccess = services.Count > 0 || context.CanCreateServices;

        var rows = new List<KeyboardButton[]>
        {
            new[] { new KeyboardButton(TelegramButtonLabels.Subscriptions), new KeyboardButton(TelegramButtonLabels.Help) },
            new[] { new KeyboardButton(TelegramButtonLabels.UnsubscribeHelp) }
        };

        if (hasServiceAccess)
        {
            rows.Add(new[] { new KeyboardButton(TelegramButtonLabels.Services), new KeyboardButton(TelegramButtonLabels.CreateServiceHelp) });
            rows.Add(new[] { new KeyboardButton(TelegramButtonLabels.TemplateHelp), new KeyboardButton(TelegramButtonLabels.InviteHelp) });
        }

        if (context.IsSuperAdmin)
        {
            rows.Add(new[] { new KeyboardButton(TelegramButtonLabels.CreatorManagement) });
        }

        return new ReplyKeyboardMarkup(rows)
        {
            ResizeKeyboard = true,
            IsPersistent = true
        };
    }

    private static InlineKeyboardMarkup BuildSubscribeConfirmationKeyboard(string inviteCode) =>
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("✅ Да", $"{SubscribeYesPrefix}{inviteCode}"),
                InlineKeyboardButton.WithCallbackData("❌ Нет", $"{SubscribeNoPrefix}{inviteCode}")
            }
        });

    private static InlineKeyboardMarkup BuildUnsubscribeMarkup(string servicePublicId) =>
        new(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    "🗑️ Отписаться",
                    $"{UnsubscribePrefix}{servicePublicId}")
            }
        });

    private static InlineKeyboardMarkup BuildSubscriptionsMarkup(IReadOnlyCollection<SubscriptionListItem> subscriptions) =>
        new(subscriptions
            .Select(x => new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"🗑️ {x.ServiceName}",
                    $"{UnsubscribePrefix}{x.ServicePublicId}")
            }));

    private static bool IsHelpCommand(string text) =>
        text.Equals("/start", StringComparison.OrdinalIgnoreCase)
        || text.Equals("/help", StringComparison.OrdinalIgnoreCase)
        || text.Equals("Помощь", StringComparison.OrdinalIgnoreCase)
        || text.Equals(TelegramButtonLabels.Help, StringComparison.OrdinalIgnoreCase)
        || text.Equals("Главное меню", StringComparison.OrdinalIgnoreCase);

    private static bool IsSubscriptionsCommand(string text) =>
        text.Equals("/subscriptions", StringComparison.OrdinalIgnoreCase)
        || text.Equals("Мои подписки", StringComparison.OrdinalIgnoreCase)
        || text.Equals(TelegramButtonLabels.Subscriptions, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnsubscribeHelpCommand(string text) =>
        text.Equals("Как отписаться", StringComparison.OrdinalIgnoreCase)
        || text.Equals(TelegramButtonLabels.UnsubscribeHelp, StringComparison.OrdinalIgnoreCase);

    private static bool IsServicesCommand(string text) =>
        text.Equals("/services", StringComparison.OrdinalIgnoreCase)
        || text.Equals("Мои сервисы", StringComparison.OrdinalIgnoreCase)
        || text.Equals(TelegramButtonLabels.Services, StringComparison.OrdinalIgnoreCase);

    private static bool IsCreateServiceHelpCommand(string text) =>
        text.Equals("Как создать сервис", StringComparison.OrdinalIgnoreCase)
        || text.Equals(TelegramButtonLabels.CreateServiceHelp, StringComparison.OrdinalIgnoreCase);

    private static bool IsTemplateHelpCommand(string text) =>
        text.Equals("Как создать шаблон", StringComparison.OrdinalIgnoreCase)
        || text.Equals(TelegramButtonLabels.TemplateHelp, StringComparison.OrdinalIgnoreCase);

    private static bool IsInviteHelpCommand(string text) =>
        text.Equals("Как создать приглашение", StringComparison.OrdinalIgnoreCase)
        || text.Equals(TelegramButtonLabels.InviteHelp, StringComparison.OrdinalIgnoreCase);

    private static bool IsCreatorManagementCommand(string text) =>
        text.Equals("Управление создателями", StringComparison.OrdinalIgnoreCase)
        || text.Equals(TelegramButtonLabels.CreatorManagement, StringComparison.OrdinalIgnoreCase);

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private string BuildInviteLink(string inviteCode)
    {
        var botUsername = telegramBotOptions.Value.BotUsername?.Trim().TrimStart('@') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(botUsername))
        {
            return string.Empty;
        }

        return $"https://t.me/{botUsername}?text={Uri.EscapeDataString(inviteCode)}";
    }

    private static string BuildLinkMarkup(string inviteLink)
    {
        if (string.IsNullOrWhiteSpace(inviteLink))
        {
            return "<i>BotUsername не задан в настройках.</i>";
        }

        var encoded = Encode(inviteLink);
        return $"<a href=\"{encoded}\">{encoded}</a>";
    }
}
