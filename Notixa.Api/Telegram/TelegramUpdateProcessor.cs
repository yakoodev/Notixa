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
    private const string StartKeyboardLabel = "▶️ Start";
    private const string SubscribeYesPrefix = "sub:yes:";
    private const string SubscribeNoPrefix = "sub:no:";
    private const string UnsubscribeAskPrefix = "unsub:ask:";
    private const string UnsubscribeYesPrefix = "unsub:yes:";
    private const string UnsubscribeNoPrefix = "unsub:no:";
    private const string HomePrefix = "screen:home";
    private const string HelpPrefix = "screen:help";
    private const string SubscriptionsPrefix = "screen:subscriptions";
    private const string ServicesPrefix = "screen:services";
    private const string CreateServiceHelpPrefix = "screen:create-service-help";
    private const string TemplateHelpPrefix = "screen:template-help";
    private const string InviteHelpPrefix = "screen:invite-help";
    private const string CreatorHelpPrefix = "screen:creator-help";
    private const string ServiceViewPrefix = "service:view:";
    private const string ServiceAdminsPrefix = "service:admins:";
    private const string ServiceGeneralInvitePrefix = "service:invite:general:";
    private const string ServicePersonalInviteHelpPrefix = "service:invite:personal-help:";

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

        var text = update.Message.Text.Trim();

        try
        {
            var screen = await HandleTextAsync(from.Id, text, cancellationToken);
            await RenderScreenAsync(from.Id, update.Message.Chat.Id, screen, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to process telegram message from {TelegramUserId}.", from.Id);
            var body = ex is DbUpdateException
                ? "Не удалось сохранить изменения в базе. Попробуйте еще раз через несколько секунд."
                : Encode(ex.Message);
            await RenderScreenAsync(
                from.Id,
                update.Message.Chat.Id,
                BuildStatusScreen("<b>⚠️ Ошибка</b>", body, await BuildHomeMarkupAsync(from.Id, cancellationToken)),
                cancellationToken);
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
            await AnswerCallbackAsync(callbackQuery.Id, null, cancellationToken);
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
            await AnswerCallbackAsync(callbackQuery.Id, "Не удалось обработать действие.", cancellationToken);
        }
    }

    private async Task RenderScreenAsync(long telegramUserId, long chatId, TelegramScreen screen, CancellationToken cancellationToken, int? preferredMessageId = null)
    {
        var result = await telegramMessageSender.SendOrEditScreenAsync(chatId, preferredMessageId, screen.Message, screen.ParseMode, cancellationToken, screen.ReplyMarkup);
        if (result.MessageId > 0)
        {
            await administrationService.SaveBotScreenStateAsync(telegramUserId, result.ChatId, result.MessageId, cancellationToken);
        }
    }

    private async Task<TelegramScreen> HandleTextAsync(long telegramUserId, string text, CancellationToken cancellationToken)
    {
        if (IsHelpCommand(text)) return await BuildHomeScreenAsync(telegramUserId, cancellationToken);
        if (IsSubscriptionsCommand(text)) return await BuildSubscriptionsScreenAsync(telegramUserId, cancellationToken);
        if (IsServicesCommand(text)) return await BuildServicesScreenAsync(telegramUserId, cancellationToken);
        if (IsUnsubscribeHelpCommand(text)) return await BuildUnsubscribeHelpScreenAsync(telegramUserId, cancellationToken);
        if (IsCreateServiceHelpCommand(text)) return await BuildCreateServiceHelpScreenAsync(telegramUserId, cancellationToken);
        if (IsTemplateHelpCommand(text)) return await BuildTemplateHelpScreenAsync(telegramUserId, cancellationToken);
        if (IsInviteHelpCommand(text)) return await BuildInviteHelpScreenAsync(telegramUserId, cancellationToken);
        if (IsCreatorManagementCommand(text)) return await BuildCreatorHelpScreenAsync(telegramUserId, cancellationToken);
        if (text.StartsWith("/allow_creator ", StringComparison.OrdinalIgnoreCase)) return await HandleAllowCreatorAsync(telegramUserId, text, true, cancellationToken);
        if (text.StartsWith("/deny_creator ", StringComparison.OrdinalIgnoreCase)) return await HandleAllowCreatorAsync(telegramUserId, text, false, cancellationToken);
        if (text.StartsWith("/create_service ", StringComparison.OrdinalIgnoreCase)) return await HandleCreateServiceAsync(telegramUserId, text, cancellationToken);
        if (text.StartsWith("/create_template ", StringComparison.OrdinalIgnoreCase)) return await HandleCreateTemplateAsync(telegramUserId, text, cancellationToken);
        if (text.StartsWith("/generate_general_invite ", StringComparison.OrdinalIgnoreCase)) return await HandleGenerateGeneralInviteAsync(telegramUserId, text, cancellationToken);
        if (text.StartsWith("/generate_personal_invite ", StringComparison.OrdinalIgnoreCase)) return await HandleGeneratePersonalInviteAsync(telegramUserId, text, cancellationToken);
        if (text.StartsWith("/service_admins ", StringComparison.OrdinalIgnoreCase)) return await BuildServiceAdminsScreenAsync(telegramUserId, text["/service_admins ".Length..].Trim(), cancellationToken);
        if (text.StartsWith("/add_service_admin ", StringComparison.OrdinalIgnoreCase)) return await HandleServiceAdminMutationAsync(telegramUserId, text, true, cancellationToken);
        if (text.StartsWith("/remove_service_admin ", StringComparison.OrdinalIgnoreCase)) return await HandleServiceAdminMutationAsync(telegramUserId, text, false, cancellationToken);
        if (text.StartsWith("/unsubscribe ", StringComparison.OrdinalIgnoreCase)) return await HandleUnsubscribeCommandAsync(telegramUserId, text, cancellationToken);
        return await HandleInvitePreviewAsync(telegramUserId, text, cancellationToken);
    }

    private async Task<TelegramScreen> HandleCallbackAsync(long telegramUserId, string data, CancellationToken cancellationToken)
    {
        if (data == HomePrefix) return await BuildHomeScreenAsync(telegramUserId, cancellationToken);
        if (data == HelpPrefix) return await BuildHelpScreenAsync(telegramUserId, cancellationToken);
        if (data == SubscriptionsPrefix) return await BuildSubscriptionsScreenAsync(telegramUserId, cancellationToken);
        if (data == ServicesPrefix) return await BuildServicesScreenAsync(telegramUserId, cancellationToken);
        if (data == CreateServiceHelpPrefix) return await BuildCreateServiceHelpScreenAsync(telegramUserId, cancellationToken);
        if (data == TemplateHelpPrefix) return await BuildTemplateHelpScreenAsync(telegramUserId, cancellationToken);
        if (data == InviteHelpPrefix) return await BuildInviteHelpScreenAsync(telegramUserId, cancellationToken);
        if (data == CreatorHelpPrefix) return await BuildCreatorHelpScreenAsync(telegramUserId, cancellationToken);
        if (data.StartsWith(ServiceViewPrefix, StringComparison.Ordinal)) return await BuildServiceDetailsScreenAsync(telegramUserId, data[ServiceViewPrefix.Length..], cancellationToken);
        if (data.StartsWith(ServiceAdminsPrefix, StringComparison.Ordinal)) return await BuildServiceAdminsScreenAsync(telegramUserId, data[ServiceAdminsPrefix.Length..], cancellationToken);
        if (data.StartsWith(ServiceGeneralInvitePrefix, StringComparison.Ordinal)) return BuildInviteCreatedScreen(await administrationService.CreateInviteAsync(telegramUserId, data[ServiceGeneralInvitePrefix.Length..], InviteCodeType.General, null, null, null, cancellationToken), "✅ Приглашение создано");
        if (data.StartsWith(ServicePersonalInviteHelpPrefix, StringComparison.Ordinal)) return await BuildPersonalInviteHelpScreenAsync(telegramUserId, data[ServicePersonalInviteHelpPrefix.Length..], cancellationToken);
        if (data.StartsWith(SubscribeYesPrefix, StringComparison.Ordinal)) return await HandleSubscribeConfirmationAsync(telegramUserId, data[SubscribeYesPrefix.Length..], cancellationToken);
        if (data.StartsWith(SubscribeNoPrefix, StringComparison.Ordinal)) return BuildStatusScreen("<b>👌 Подписка отменена</b>", "Когда будете готовы, просто отправьте код приглашения еще раз.", await BuildHomeMarkupAsync(telegramUserId, cancellationToken));
        if (data.StartsWith(UnsubscribeAskPrefix, StringComparison.Ordinal)) return await BuildUnsubscribeConfirmScreenAsync(telegramUserId, data[UnsubscribeAskPrefix.Length..], cancellationToken);
        if (data.StartsWith(UnsubscribeYesPrefix, StringComparison.Ordinal)) return await HandleUnsubscribeCallbackAsync(telegramUserId, data[UnsubscribeYesPrefix.Length..], cancellationToken);
        if (data.StartsWith(UnsubscribeNoPrefix, StringComparison.Ordinal)) return await BuildSubscriptionsScreenAsync(telegramUserId, cancellationToken);
        return await BuildHomeScreenAsync(telegramUserId, cancellationToken);
    }

    private async Task<TelegramScreen> HandleAllowCreatorAsync(long telegramUserId, string text, bool allowed, CancellationToken cancellationToken)
    {
        var userId = ParseLongArgument(text);
        var success = await administrationService.SetCreatorPermissionAsync(telegramUserId, userId, allowed, cancellationToken);
        return BuildStatusScreen(
            success ? (allowed ? "<b>✅ Доступ выдан</b>" : "<b>✅ Доступ отозван</b>") : "<b>⛔ Доступ запрещен</b>",
            success
                ? allowed ? $"Пользователь <code>{userId}</code> теперь может создавать сервисы." : $"Пользователь <code>{userId}</code> больше не может создавать сервисы."
                : "Только супер-админ может управлять создателями сервисов.",
            await BuildCreatorHelpMarkupAsync(telegramUserId, cancellationToken));
    }

    private async Task<TelegramScreen> HandleCreateServiceAsync(long telegramUserId, string text, CancellationToken cancellationToken)
    {
        var parts = text["/create_service ".Length..].Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length < 2) throw new InvalidOperationException("Использование: /create_service Название | Описание");
        var result = await administrationService.CreateServiceAsync(telegramUserId, parts[0], parts[1], cancellationToken);
        return new TelegramScreen($"""
            <b>✅ Сервис создан</b>
            Идентификатор сервиса: <code>{Encode(result.PublicId)}</code>
            Ключ сервиса: <code>{Encode(result.ServiceKey)}</code>

            <b>⚠️ Сохраните ключ сервиса прямо сейчас.</b>
            <b>Позже получить этот ключ повторно будет нельзя.</b>
            """, TemplateParseMode.Html, BuildServiceActionsMarkup(result.PublicId, parts[0].Trim()));
    }

    private async Task<TelegramScreen> HandleCreateTemplateAsync(long telegramUserId, string text, CancellationToken cancellationToken)
    {
        var parts = text["/create_template ".Length..].Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length < 2) throw new InvalidOperationException("Использование: /create_template serviceId templateKey parseMode | текст шаблона");
        var head = parts[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (head.Length < 3) throw new InvalidOperationException("Использование: /create_template serviceId templateKey parseMode | текст шаблона");
        var parseMode = Enum.Parse<TemplateParseMode>(head[2], true);
        var result = await administrationService.UpsertTemplateAsync(telegramUserId, head[0], head[1], parseMode, parts[1], cancellationToken);
        return BuildStatusScreen("<b>✅ Шаблон сохранен</b>", $"Сервис: <code>{Encode(result.ServicePublicId)}</code>\nШаблон: <code>{Encode(result.TemplateKey)}</code>", BuildServiceNavigationMarkup(result.ServicePublicId));
    }

    private async Task<TelegramScreen> HandleGenerateGeneralInviteAsync(long telegramUserId, string text, CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) throw new InvalidOperationException("Использование: /generate_general_invite serviceId [usageLimit] [expiresHours]");
        if (parts.Length >= 3 && !int.TryParse(parts[2], out _)) throw new InvalidOperationException("Для общего приглашения второй параметр должен быть числом.");
        if (parts.Length >= 4 && !int.TryParse(parts[3], out _)) throw new InvalidOperationException("Параметр expiresHours должен быть числом.");
        int? usageLimit = parts.Length >= 3 ? int.Parse(parts[2]) : null;
        int? expiresHours = parts.Length >= 4 ? int.Parse(parts[3]) : null;
        return BuildInviteCreatedScreen(await administrationService.CreateInviteAsync(telegramUserId, parts[1], InviteCodeType.General, null, usageLimit, expiresHours, cancellationToken), "✅ Приглашение создано");
    }

    private async Task<TelegramScreen> HandleGeneratePersonalInviteAsync(long telegramUserId, string text, CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) throw new InvalidOperationException("Использование: /generate_personal_invite serviceId externalUserKey [expiresHours]");
        if (parts.Length >= 4 && !int.TryParse(parts[3], out _)) throw new InvalidOperationException("Параметр expiresHours должен быть числом.");
        int? expiresHours = parts.Length >= 4 ? int.Parse(parts[3]) : null;
        return BuildInviteCreatedScreen(await administrationService.CreateInviteAsync(telegramUserId, parts[1], InviteCodeType.Personal, parts[2], 1, expiresHours, cancellationToken), "✅ Персональное приглашение создано");
    }

    private async Task<TelegramScreen> HandleServiceAdminMutationAsync(long telegramUserId, string text, bool add, CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) throw new InvalidOperationException(add ? "Usage: /add_service_admin serviceId telegramUserId" : "Usage: /remove_service_admin serviceId telegramUserId");
        if (add) await administrationService.AddServiceAdminAsync(telegramUserId, parts[1], long.Parse(parts[2]), cancellationToken);
        else await administrationService.RemoveServiceAdminAsync(telegramUserId, parts[1], long.Parse(parts[2]), cancellationToken);
        return BuildStatusScreen(add ? "<b>✅ Администратор сервиса добавлен</b>" : "<b>✅ Администратор сервиса удален</b>", $"Сервис: <code>{Encode(parts[1])}</code>", BuildServiceNavigationMarkup(parts[1]));
    }

    private async Task<TelegramScreen> HandleUnsubscribeCommandAsync(long telegramUserId, string text, CancellationToken cancellationToken)
    {
        var serviceId = text["/unsubscribe ".Length..].Trim();
        var removed = await administrationService.UnsubscribeAsync(telegramUserId, serviceId, cancellationToken);
        return BuildStatusScreen(removed ? "<b>✅ Подписка отключена</b>" : "<b>⚠️ Отписка не выполнена</b>", removed ? $"Сервис: <code>{Encode(serviceId)}</code>" : $"Активная подписка для сервиса <code>{Encode(serviceId)}</code> не найдена.", await BuildSubscriptionsMarkupAsync(telegramUserId, cancellationToken));
    }

    private async Task<TelegramScreen> HandleInvitePreviewAsync(long telegramUserId, string text, CancellationToken cancellationToken)
    {
        var preview = await administrationService.PreviewInviteAsync(telegramUserId, text, cancellationToken);
        return preview.Status switch
        {
            PreviewInviteStatus.Invalid => BuildStatusScreen("<b>⚠️ Код не принят</b>", "Код приглашения недействителен, истек или уже исчерпан.", await BuildHomeMarkupAsync(telegramUserId, cancellationToken)),
            PreviewInviteStatus.AlreadySubscribed => new TelegramScreen($"""
                <b>ℹ️ Вы уже подписаны на этот сервис</b>
                Сервис: <b>{Encode(preview.ServiceName!)}</b> <code>{Encode(preview.ServicePublicId!)}</code>
                Команда для отписки:
                <code>/unsubscribe {Encode(preview.ServicePublicId!)}</code>
                """, TemplateParseMode.Html, BuildUnsubscribeMarkup(preview.ServicePublicId!, preview.ServiceName!)),
            _ => new TelegramScreen($"""
                <b>🔔 Подписка на сервис</b>
                Вы хотите подписаться на уведомления от сервиса <b>{Encode(preview.ServiceName!)}</b>?

                Идентификатор сервиса: <code>{Encode(preview.ServicePublicId!)}</code>
                """, TemplateParseMode.Html, BuildSubscribeConfirmationKeyboard(text))
        };
    }

    private async Task<TelegramScreen> HandleSubscribeConfirmationAsync(long telegramUserId, string inviteCode, CancellationToken cancellationToken)
    {
        var redeemResult = await administrationService.RedeemInviteAsync(telegramUserId, inviteCode, cancellationToken);
        return redeemResult.Status switch
        {
            RedeemInviteStatus.Created => new TelegramScreen($"<b>✅ Подписка подключена</b>\nСервис: <b>{Encode(redeemResult.Subscription!.ServiceName)}</b> <code>{Encode(redeemResult.Subscription.ServicePublicId)}</code>\nКоманда для отписки:\n<code>/unsubscribe {Encode(redeemResult.Subscription.ServicePublicId)}</code>", TemplateParseMode.Html, BuildUnsubscribeMarkup(redeemResult.Subscription.ServicePublicId, redeemResult.Subscription.ServiceName)),
            RedeemInviteStatus.AlreadySubscribed => new TelegramScreen($"<b>ℹ️ Вы уже подписаны</b>\nСервис: <b>{Encode(redeemResult.Subscription!.ServiceName)}</b> <code>{Encode(redeemResult.Subscription.ServicePublicId)}</code>\nКоманда для отписки:\n<code>/unsubscribe {Encode(redeemResult.Subscription.ServicePublicId)}</code>", TemplateParseMode.Html, BuildUnsubscribeMarkup(redeemResult.Subscription.ServicePublicId, redeemResult.Subscription.ServiceName)),
            _ => BuildStatusScreen("<b>⚠️ Подписка не выполнена</b>", "Приглашение недействительно, уже истекло или исчерпано.", await BuildHomeMarkupAsync(telegramUserId, cancellationToken))
        };
    }

    private async Task<TelegramScreen> BuildUnsubscribeConfirmScreenAsync(long telegramUserId, string serviceId, CancellationToken cancellationToken)
    {
        var subscription = (await administrationService.GetSubscriptionsAsync(telegramUserId, cancellationToken)).SingleOrDefault(x => x.ServicePublicId == serviceId)
            ?? throw new InvalidOperationException("Подписка не найдена.");
        return new TelegramScreen($"""
            <b>🗑️ Подтверждение отписки</b>
            Вы хотите отписаться от сервиса <b>{Encode(subscription.ServiceName)}</b>?
            """, TemplateParseMode.Html, BuildUnsubscribeConfirmationMarkup(serviceId));
    }

    private async Task<TelegramScreen> HandleUnsubscribeCallbackAsync(long telegramUserId, string serviceId, CancellationToken cancellationToken)
    {
        var removed = await administrationService.UnsubscribeAsync(telegramUserId, serviceId, cancellationToken);
        return BuildStatusScreen(removed ? "<b>✅ Подписка отключена</b>" : "<b>⚠️ Отписка не выполнена</b>", removed ? $"Сервис: <code>{Encode(serviceId)}</code>" : $"Активная подписка для сервиса <code>{Encode(serviceId)}</code> не найдена.", await BuildSubscriptionsMarkupAsync(telegramUserId, cancellationToken));
    }

    private async Task<TelegramScreen> BuildSubscriptionsScreenAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var subscriptions = await administrationService.GetSubscriptionsAsync(telegramUserId, cancellationToken);
        if (subscriptions.Count == 0)
        {
            return new TelegramScreen("<b>📬 Мои подписки</b>\nУ вас пока нет активных подписок.\n\nОтправьте код приглашения, чтобы подключить сервис.", TemplateParseMode.Html, await BuildSubscriptionsMarkupAsync(telegramUserId, cancellationToken));
        }
        var lines = subscriptions.Select(x => $"• <b>{Encode(x.ServiceName)}</b> <code>{Encode(x.ServicePublicId)}</code>\nКоманда для отписки:\n<code>/unsubscribe {Encode(x.ServicePublicId)}</code>");
        return new TelegramScreen($"<b>📬 Мои подписки</b>\n{string.Join($"{Environment.NewLine}{Environment.NewLine}", lines)}", TemplateParseMode.Html, await BuildSubscriptionsMarkupAsync(telegramUserId, cancellationToken, subscriptions));
    }

    private async Task<TelegramScreen> BuildServicesScreenAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var services = await administrationService.GetManagedServicesAsync(telegramUserId, cancellationToken);
        if (services.Count == 0)
        {
            return new TelegramScreen("<b>🛠️ Мои сервисы</b>\nУ вас пока нет сервисов или прав администратора.", TemplateParseMode.Html, await BuildServicesMarkupAsync(telegramUserId, cancellationToken));
        }
        var lines = services.Select(x => $"• <b>{Encode(x.Name)}</b> <code>{Encode(x.PublicId)}</code>\n{Encode(x.Description)}");
        return new TelegramScreen($"<b>🛠️ Мои сервисы</b>\n{string.Join($"{Environment.NewLine}{Environment.NewLine}", lines)}", TemplateParseMode.Html, await BuildServicesMarkupAsync(telegramUserId, cancellationToken, services));
    }

    private async Task<TelegramScreen> BuildServiceDetailsScreenAsync(long telegramUserId, string serviceId, CancellationToken cancellationToken)
    {
        var service = (await administrationService.GetManagedServicesAsync(telegramUserId, cancellationToken)).SingleOrDefault(x => x.PublicId == serviceId)
            ?? throw new InvalidOperationException("Сервис не найден.");
        return new TelegramScreen($"<b>⚙️ {Encode(service.Name)}</b>\n<code>{Encode(service.PublicId)}</code>\n\n{Encode(service.Description)}", TemplateParseMode.Html, BuildServiceActionsMarkup(service.PublicId, service.Name));
    }

    private async Task<TelegramScreen> BuildServiceAdminsScreenAsync(long telegramUserId, string serviceId, CancellationToken cancellationToken)
    {
        var admins = await administrationService.GetServiceAdminsAsync(telegramUserId, serviceId, cancellationToken);
        var body = admins.Count == 0 ? "Администраторы пока не назначены." : string.Join(Environment.NewLine, admins.Select(x => $"• <code>{x}</code>"));
        return new TelegramScreen($"<b>👥 Администраторы сервиса</b>\n{body}", TemplateParseMode.Html, BuildServiceNavigationMarkup(serviceId));
    }

    private async Task<TelegramScreen> BuildHomeScreenAsync(long telegramUserId, CancellationToken cancellationToken) =>
        new("""
            <b>🔔 Notixa</b>
            Отправьте код приглашения, чтобы подписаться на уведомления.

            Ниже основные разделы управления.
            """, TemplateParseMode.Html, await BuildHomeMarkupAsync(telegramUserId, cancellationToken));

    private async Task<TelegramScreen> BuildHelpScreenAsync(long telegramUserId, CancellationToken cancellationToken) =>
        new("""
            <b>❓ Помощь</b>
            Notixa подключает Telegram-уведомления для ваших сервисов.

            Что можно сделать:
            • подключить сервис по коду приглашения
            • посмотреть свои подписки
            • управлять сервисами и приглашениями

            Если нужен ввод команды, экран покажет готовый формат.
            """, TemplateParseMode.Html, await BuildHelpMarkupAsync(telegramUserId, cancellationToken));

    private async Task<TelegramScreen> BuildCreateServiceHelpScreenAsync(long telegramUserId, CancellationToken cancellationToken) =>
        new("""
            <b>➕ Как создать сервис</b>
            Отправьте сообщение в формате:
            <code>/create_service Название | Описание</code>

            Пример:
            <code>/create_service Orders | Уведомления по заказам</code>
            """, TemplateParseMode.Html, await BuildHelpMarkupAsync(telegramUserId, cancellationToken));

    private async Task<TelegramScreen> BuildTemplateHelpScreenAsync(long telegramUserId, CancellationToken cancellationToken) =>
        new("""
            <b>🧩 Как создать шаблон</b>
            Отправьте сообщение в формате:
            <code>/create_template serviceId templateKey PlainText | Текст {{переменная}}</code>
            """, TemplateParseMode.Html, await BuildHelpMarkupAsync(telegramUserId, cancellationToken));

    private async Task<TelegramScreen> BuildInviteHelpScreenAsync(long telegramUserId, CancellationToken cancellationToken) =>
        new("""
            <b>🎟️ Как создать приглашение</b>
            Откройте сервис через <b>🛠️ Мои сервисы</b> и используйте кнопки действий.

            Либо команды:
            <code>/generate_general_invite serviceId</code>
            <code>/generate_personal_invite serviceId externalUserKey [expiresHours]</code>
            """, TemplateParseMode.Html, await BuildHelpMarkupAsync(telegramUserId, cancellationToken));

    private Task<TelegramScreen> BuildPersonalInviteHelpScreenAsync(long telegramUserId, string serviceId, CancellationToken cancellationToken) =>
        Task.FromResult(new TelegramScreen($"""
            <b>👤 Персональное приглашение</b>
            Отправьте сообщение в формате:
            <code>/generate_personal_invite {Encode(serviceId)} externalUserKey [expiresHours]</code>
            """, TemplateParseMode.Html, BuildServiceNavigationMarkup(serviceId)));

    private async Task<TelegramScreen> BuildCreatorHelpScreenAsync(long telegramUserId, CancellationToken cancellationToken) =>
        new("""
            <b>👑 Управление создателями сервисов</b>
            <code>/allow_creator telegramUserId</code>
            <code>/deny_creator telegramUserId</code>
            """, TemplateParseMode.Html, await BuildCreatorHelpMarkupAsync(telegramUserId, cancellationToken));

    private async Task<TelegramScreen> BuildUnsubscribeHelpScreenAsync(long telegramUserId, CancellationToken cancellationToken) =>
        new("""
            <b>➖ Как отписаться</b>
            Откройте раздел <b>📬 Мои подписки</b> и нажмите кнопку отписки под нужным сервисом.

            Если нужно вручную:
            <code>/unsubscribe serviceId</code>
            """, TemplateParseMode.Html, await BuildSubscriptionsMarkupAsync(telegramUserId, cancellationToken));

    private TelegramScreen BuildInviteCreatedScreen(CreateInviteResult result, string title)
    {
        var inviteLink = BuildInviteLink(result.InviteCode);
        return new TelegramScreen($"""
            <b>{title}</b>
            Сервис: <code>{Encode(result.ServicePublicId)}</code>
            Код: <code>{Encode(result.InviteCode)}</code>
            Ссылка: {BuildLinkMarkup(inviteLink)}
            """, TemplateParseMode.Html, BuildServiceNavigationMarkup(result.ServicePublicId));
    }

    private static TelegramScreen BuildStatusScreen(string title, string body, ReplyMarkup? replyMarkup) => new($"{title}\n{body}", TemplateParseMode.Html, replyMarkup);

    private async Task<InlineKeyboardMarkup> BuildHomeMarkupAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var context = await userContextService.GetUserContextAsync(telegramUserId, cancellationToken);
        var services = await administrationService.GetManagedServicesAsync(telegramUserId, cancellationToken);
        var hasServiceAccess = services.Count > 0 || context.CanCreateServices;
        var rows = new List<InlineKeyboardButton[]>
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📬 Мои подписки", SubscriptionsPrefix),
                InlineKeyboardButton.WithCallbackData("❓ Помощь", HelpPrefix)
            }
        };
        if (hasServiceAccess)
        {
            rows.Add(
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🛠️ Мои сервисы", ServicesPrefix),
                    InlineKeyboardButton.WithCallbackData("➕ Создать сервис", CreateServiceHelpPrefix)
                });
            rows.Add(
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🧩 Шаблоны", TemplateHelpPrefix),
                    InlineKeyboardButton.WithCallbackData("🎟️ Приглашения", InviteHelpPrefix)
                });
        }
        if (context.IsSuperAdmin)
        {
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("👑 Создатели", CreatorHelpPrefix) });
        }
        return new InlineKeyboardMarkup(rows);
    }

    private async Task<InlineKeyboardMarkup> BuildHelpMarkupAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var rows = new List<InlineKeyboardButton[]>
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🏠 Главное меню", HomePrefix),
                InlineKeyboardButton.WithCallbackData("📬 Мои подписки", SubscriptionsPrefix)
            }
        };
        var context = await userContextService.GetUserContextAsync(telegramUserId, cancellationToken);
        var services = await administrationService.GetManagedServicesAsync(telegramUserId, cancellationToken);
        if (services.Count > 0 || context.CanCreateServices)
        {
            rows.Add(
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🛠️ Мои сервисы", ServicesPrefix),
                    InlineKeyboardButton.WithCallbackData("🎟️ Приглашения", InviteHelpPrefix)
                });
        }
        return new InlineKeyboardMarkup(rows);
    }

    private async Task<InlineKeyboardMarkup> BuildCreatorHelpMarkupAsync(long telegramUserId, CancellationToken cancellationToken) => await BuildHomeMarkupAsync(telegramUserId, cancellationToken);

    private async Task<InlineKeyboardMarkup> BuildSubscriptionsMarkupAsync(long telegramUserId, CancellationToken cancellationToken, IReadOnlyCollection<SubscriptionListItem>? subscriptions = null)
    {
        subscriptions ??= await administrationService.GetSubscriptionsAsync(telegramUserId, cancellationToken);
        var rows = subscriptions.Select(x => new[] { InlineKeyboardButton.WithCallbackData($"🗑️ Отписаться от {ShortLabel(x.ServiceName)}", $"{UnsubscribeAskPrefix}{x.ServicePublicId}") }).ToList();
        rows.Add(
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🏠 Главное меню", HomePrefix),
                InlineKeyboardButton.WithCallbackData("❓ Помощь", HelpPrefix)
            });
        return new InlineKeyboardMarkup(rows);
    }

    private async Task<InlineKeyboardMarkup> BuildServicesMarkupAsync(long telegramUserId, CancellationToken cancellationToken, IReadOnlyCollection<ServiceListItem>? services = null)
    {
        services ??= await administrationService.GetManagedServicesAsync(telegramUserId, cancellationToken);
        var rows = services.Select(x => new[] { InlineKeyboardButton.WithCallbackData($"⚙️ {ShortLabel(x.Name)}", $"{ServiceViewPrefix}{x.PublicId}") }).ToList();
        rows.Add(
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🏠 Главное меню", HomePrefix),
                InlineKeyboardButton.WithCallbackData("➕ Создать сервис", CreateServiceHelpPrefix)
            });
        return new InlineKeyboardMarkup(rows);
    }

    private static InlineKeyboardMarkup BuildServiceActionsMarkup(string serviceId, string serviceName) => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData("🎟️ Общее приглашение", $"{ServiceGeneralInvitePrefix}{serviceId}"), InlineKeyboardButton.WithCallbackData("👤 Персональное приглашение", $"{ServicePersonalInviteHelpPrefix}{serviceId}") },
        new[] { InlineKeyboardButton.WithCallbackData("👥 Админы", $"{ServiceAdminsPrefix}{serviceId}"), InlineKeyboardButton.WithCallbackData($"🗑️ Отписка от {ShortLabel(serviceName)}", $"{UnsubscribeAskPrefix}{serviceId}") },
        new[] { InlineKeyboardButton.WithCallbackData("⬅️ К сервисам", ServicesPrefix), InlineKeyboardButton.WithCallbackData("🏠 Главное меню", HomePrefix) }
    });

    private static InlineKeyboardMarkup BuildServiceNavigationMarkup(string serviceId) => new(new[] { new[] { InlineKeyboardButton.WithCallbackData("⬅️ К сервису", $"{ServiceViewPrefix}{serviceId}"), InlineKeyboardButton.WithCallbackData("🏠 Главное меню", HomePrefix) } });
    private static InlineKeyboardMarkup BuildSubscribeConfirmationKeyboard(string inviteCode) => new(new[] { new[] { InlineKeyboardButton.WithCallbackData("✅ Да", $"{SubscribeYesPrefix}{inviteCode}"), InlineKeyboardButton.WithCallbackData("❌ Нет", $"{SubscribeNoPrefix}{inviteCode}") } });
    private static InlineKeyboardMarkup BuildUnsubscribeMarkup(string serviceId, string serviceName) => new(new[]
    {
        new[] { InlineKeyboardButton.WithCallbackData($"🗑️ Отписаться от {ShortLabel(serviceName)}", $"{UnsubscribeAskPrefix}{serviceId}") },
        new[] { InlineKeyboardButton.WithCallbackData("📬 Мои подписки", SubscriptionsPrefix), InlineKeyboardButton.WithCallbackData("🏠 Главное меню", HomePrefix) }
    });
    private static InlineKeyboardMarkup BuildUnsubscribeConfirmationMarkup(string serviceId) => new(new[] { new[] { InlineKeyboardButton.WithCallbackData("✅ Да", $"{UnsubscribeYesPrefix}{serviceId}"), InlineKeyboardButton.WithCallbackData("❌ Нет", $"{UnsubscribeNoPrefix}{serviceId}") } });

    private async Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken cancellationToken)
    {
        if (!botClientAccessor.IsConfigured) return;
        await botClientAccessor.Client!.AnswerCallbackQuery(callbackQueryId, text: text, cancellationToken: cancellationToken);
    }

    private static long ParseLongArgument(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !long.TryParse(parts[1], out var userId)) throw new InvalidOperationException("Expected a single numeric Telegram user id.");
        return userId;
    }

    private static bool IsHelpCommand(string text) => IsStartCommand(text) || text.Equals("/help", StringComparison.OrdinalIgnoreCase) || text.Equals("Помощь", StringComparison.OrdinalIgnoreCase) || text.Equals("❓ Помощь", StringComparison.OrdinalIgnoreCase) || text.Equals("Главное меню", StringComparison.OrdinalIgnoreCase);
    private static bool IsSubscriptionsCommand(string text) => text.Equals("/subscriptions", StringComparison.OrdinalIgnoreCase) || text.Equals("Мои подписки", StringComparison.OrdinalIgnoreCase) || text.Equals("📬 Мои подписки", StringComparison.OrdinalIgnoreCase);
    private static bool IsUnsubscribeHelpCommand(string text) => text.Equals("Как отписаться", StringComparison.OrdinalIgnoreCase) || text.Equals("➖ Как отписаться", StringComparison.OrdinalIgnoreCase);
    private static bool IsServicesCommand(string text) => text.Equals("/services", StringComparison.OrdinalIgnoreCase) || text.Equals("Мои сервисы", StringComparison.OrdinalIgnoreCase) || text.Equals("🛠️ Мои сервисы", StringComparison.OrdinalIgnoreCase);
    private static bool IsCreateServiceHelpCommand(string text) => text.Equals("Как создать сервис", StringComparison.OrdinalIgnoreCase) || text.Equals("➕ Как создать сервис", StringComparison.OrdinalIgnoreCase);
    private static bool IsTemplateHelpCommand(string text) => text.Equals("Как создать шаблон", StringComparison.OrdinalIgnoreCase) || text.Equals("🧩 Как создать шаблон", StringComparison.OrdinalIgnoreCase);
    private static bool IsInviteHelpCommand(string text) => text.Equals("Как создать приглашение", StringComparison.OrdinalIgnoreCase) || text.Equals("🎟️ Как создать приглашение", StringComparison.OrdinalIgnoreCase);
    private static bool IsCreatorManagementCommand(string text) => text.Equals("Управление создателями", StringComparison.OrdinalIgnoreCase) || text.Equals("👑 Управление создателями", StringComparison.OrdinalIgnoreCase);
    private static bool IsStartCommand(string text) => text.Equals("/start", StringComparison.OrdinalIgnoreCase) || text.Equals(StartKeyboardLabel, StringComparison.OrdinalIgnoreCase) || text.Equals("Start", StringComparison.OrdinalIgnoreCase);
    private static string ShortLabel(string text) => text.Length <= 24 ? text : $"{text[..21]}...";
    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private string BuildInviteLink(string inviteCode)
    {
        var botUsername = telegramBotOptions.Value.BotUsername?.Trim().TrimStart('@') ?? string.Empty;
        return string.IsNullOrWhiteSpace(botUsername) ? string.Empty : $"https://t.me/{botUsername}?text={Uri.EscapeDataString(inviteCode)}";
    }

    private static string BuildLinkMarkup(string inviteLink)
    {
        if (string.IsNullOrWhiteSpace(inviteLink)) return "<i>BotUsername не задан в настройках.</i>";
        var encoded = Encode(inviteLink);
        return $"<a href=\"{encoded}\">{encoded}</a>";
    }

    private sealed record TelegramScreen(string Message, TemplateParseMode ParseMode, ReplyMarkup? ReplyMarkup = null);
}
