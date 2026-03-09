using System.Text.Json;
using Notixa.Api.Contracts;
using Notixa.Api.Data;
using Notixa.Api.Domain.Entities;
using Notixa.Api.Domain.Enums;
using Notixa.Api.Telegram;
using Telegram.Bot.Types.ReplyMarkups;

namespace Notixa.Api.Services;

public sealed class BotConversationService(
    ApplicationDbContext dbContext,
    IPlatformAdministrationService administrationService,
    ITimeProvider timeProvider) : IBotConversationService
{
    private const string CreateServiceFlow = "CreateService";
    private const string CreateTemplateFlow = "CreateTemplate";
    private const string PersonalInviteFlow = "PersonalInvite";
    private const string AddAdminFlow = "AddAdmin";
    private const string RemoveAdminFlow = "RemoveAdmin";
    private const string CreatorPermissionFlow = "CreatorPermission";

    private const string CreateServiceNameStep = "CreateServiceName";
    private const string CreateServiceDescriptionStep = "CreateServiceDescription";
    private const string CreateServiceConfirmStep = "CreateServiceConfirm";
    private const string CreateTemplateParseModeStep = "CreateTemplateParseMode";
    private const string CreateTemplateKeyStep = "CreateTemplateKey";
    private const string CreateTemplateBodyStep = "CreateTemplateBody";
    private const string PersonalInviteExternalKeyStep = "PersonalInviteExternalKey";
    private const string PersonalInviteExpiryChoiceStep = "PersonalInviteExpiryChoice";
    private const string PersonalInviteExpiryManualStep = "PersonalInviteExpiryManual";
    private const string AddAdminUserIdStep = "AddAdminUserId";
    private const string RemoveAdminSelectStep = "RemoveAdminSelect";
    private const string RemoveAdminUserIdStep = "RemoveAdminUserId";
    private const string CreatorPermissionUserIdStep = "CreatorPermissionUserId";

    public async Task<BotScreen?> HandleMessageAsync(long telegramUserId, string text, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(telegramUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(user.ActiveBotFlowType) || string.IsNullOrWhiteSpace(user.ActiveBotFlowStep))
        {
            return null;
        }

        var context = DeserializeContext(user.ActiveBotFlowContextJson);
        return user.ActiveBotFlowType switch
        {
            CreateServiceFlow => await HandleCreateServiceMessageAsync(user, context, text, cancellationToken),
            CreateTemplateFlow => await HandleCreateTemplateMessageAsync(user, context, text, cancellationToken),
            PersonalInviteFlow => await HandlePersonalInviteMessageAsync(user, context, text, cancellationToken),
            AddAdminFlow => await HandleAddAdminMessageAsync(user, context, text, cancellationToken),
            RemoveAdminFlow => await HandleRemoveAdminMessageAsync(user, context, text, cancellationToken),
            CreatorPermissionFlow => await HandleCreatorPermissionMessageAsync(user, context, text, cancellationToken),
            _ => null
        };
    }

    public async Task<BotScreen?> HandleCallbackAsync(long telegramUserId, string data, CancellationToken cancellationToken)
    {
        return data switch
        {
            BotCallbackKeys.FlowStartCreateService => await StartCreateServiceFlowAsync(telegramUserId, cancellationToken),
            BotCallbackKeys.FlowCancel => await CancelFlowAsync(telegramUserId, cancellationToken),
            BotCallbackKeys.FlowBack => await GoBackAsync(telegramUserId, cancellationToken),
            BotCallbackKeys.FlowCreateServiceConfirm => await ConfirmCreateServiceAsync(telegramUserId, cancellationToken),
            BotCallbackKeys.FlowStartCreatorAllow => await StartCreatorPermissionFlowAsync(telegramUserId, true, cancellationToken),
            BotCallbackKeys.FlowStartCreatorDeny => await StartCreatorPermissionFlowAsync(telegramUserId, false, cancellationToken),
            BotCallbackKeys.FlowPersonalInviteExpiryNone => await FinalizePersonalInviteWithoutExpiryAsync(telegramUserId, cancellationToken),
            BotCallbackKeys.FlowPersonalInviteExpiryManual => await SwitchPersonalInviteToManualExpiryAsync(telegramUserId, cancellationToken),
            BotCallbackKeys.FlowRemoveAdminManual => await SwitchRemoveAdminToManualEntryAsync(telegramUserId, cancellationToken),
            _ when data.StartsWith(BotCallbackKeys.FlowStartCreateTemplatePrefix, StringComparison.Ordinal) => await StartCreateTemplateFlowAsync(telegramUserId, data[BotCallbackKeys.FlowStartCreateTemplatePrefix.Length..], cancellationToken),
            _ when data.StartsWith(BotCallbackKeys.FlowStartPersonalInvitePrefix, StringComparison.Ordinal) => await StartPersonalInviteFlowAsync(telegramUserId, data[BotCallbackKeys.FlowStartPersonalInvitePrefix.Length..], cancellationToken),
            _ when data.StartsWith(BotCallbackKeys.FlowStartAddAdminPrefix, StringComparison.Ordinal) => await StartAddAdminFlowAsync(telegramUserId, data[BotCallbackKeys.FlowStartAddAdminPrefix.Length..], cancellationToken),
            _ when data.StartsWith(BotCallbackKeys.FlowStartRemoveAdminPrefix, StringComparison.Ordinal) => await StartRemoveAdminFlowAsync(telegramUserId, data[BotCallbackKeys.FlowStartRemoveAdminPrefix.Length..], cancellationToken),
            _ when data.StartsWith(BotCallbackKeys.FlowTemplateParseModePrefix, StringComparison.Ordinal) => await SelectTemplateParseModeAsync(telegramUserId, data[BotCallbackKeys.FlowTemplateParseModePrefix.Length..], cancellationToken),
            _ when data.StartsWith(BotCallbackKeys.FlowRemoveAdminSelectPrefix, StringComparison.Ordinal) => await RemoveSelectedAdminAsync(telegramUserId, data[BotCallbackKeys.FlowRemoveAdminSelectPrefix.Length..], cancellationToken),
            _ => null
        };
    }

    public async Task ClearAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FindAsync([telegramUserId], cancellationToken);
        if (user is null)
        {
            return;
        }

        ClearFlow(user);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<BotScreen> StartCreateServiceFlowAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        await SetFlowAsync(telegramUserId, CreateServiceFlow, CreateServiceNameStep, new ConversationContext(), cancellationToken);
        return BuildPromptScreen(
            "<b>➕ Новый сервис</b>\nВведите название сервиса.",
            BuildCancelMarkup());
    }

    private async Task<BotScreen> StartCreateTemplateFlowAsync(long telegramUserId, string servicePublicId, CancellationToken cancellationToken)
    {
        await SetFlowAsync(
            telegramUserId,
            CreateTemplateFlow,
            CreateTemplateParseModeStep,
            new ConversationContext { ServicePublicId = servicePublicId },
            cancellationToken);

        return new BotScreen(
            "<b>🧩 Новый шаблон</b>\nВыберите parse mode.",
            TemplateParseMode.Html,
            BuildParseModeMarkup());
    }

    private async Task<BotScreen> StartPersonalInviteFlowAsync(long telegramUserId, string servicePublicId, CancellationToken cancellationToken)
    {
        await SetFlowAsync(
            telegramUserId,
            PersonalInviteFlow,
            PersonalInviteExternalKeyStep,
            new ConversationContext { ServicePublicId = servicePublicId },
            cancellationToken);

        return BuildPromptScreen(
            "<b>👤 Персональное приглашение</b>\nВведите `externalUserKey` одним сообщением.",
            BuildCancelMarkup());
    }

    private async Task<BotScreen> StartAddAdminFlowAsync(long telegramUserId, string servicePublicId, CancellationToken cancellationToken)
    {
        await SetFlowAsync(
            telegramUserId,
            AddAdminFlow,
            AddAdminUserIdStep,
            new ConversationContext { ServicePublicId = servicePublicId },
            cancellationToken);

        return BuildPromptScreen(
            "<b>👥 Добавление администратора</b>\nВведите Telegram user id пользователя.",
            BuildCancelMarkup());
    }

    private async Task<BotScreen> StartRemoveAdminFlowAsync(long telegramUserId, string servicePublicId, CancellationToken cancellationToken)
    {
        var admins = await administrationService.GetServiceAdminsAsync(telegramUserId, servicePublicId, cancellationToken);
        await SetFlowAsync(
            telegramUserId,
            RemoveAdminFlow,
            RemoveAdminSelectStep,
            new ConversationContext { ServicePublicId = servicePublicId },
            cancellationToken);

        if (admins.Count == 0)
        {
            return new BotScreen(
                "<b>👥 Удаление администратора</b>\nСписок администраторов пуст.",
                TemplateParseMode.Html,
                BuildServiceNavigationMarkup(servicePublicId));
        }

        return new BotScreen(
            "<b>👥 Удаление администратора</b>\nВыберите пользователя из списка или перейдите к ручному вводу.",
            TemplateParseMode.Html,
            BuildRemoveAdminMarkup(servicePublicId, admins));
    }

    private async Task<BotScreen> StartCreatorPermissionFlowAsync(long telegramUserId, bool allowed, CancellationToken cancellationToken)
    {
        await SetFlowAsync(
            telegramUserId,
            CreatorPermissionFlow,
            CreatorPermissionUserIdStep,
            new ConversationContext { CreatorPermissionAllowed = allowed },
            cancellationToken);

        var title = allowed ? "<b>👑 Разрешить создание сервисов</b>" : "<b>👑 Запретить создание сервисов</b>";
        return BuildPromptScreen(
            $"{title}\nВведите Telegram user id пользователя.",
            BuildCancelMarkup(BotCallbackKeys.CreatorManagement));
    }

    private async Task<BotScreen> HandleCreateServiceMessageAsync(AppUser user, ConversationContext context, string text, CancellationToken cancellationToken)
    {
        if (user.ActiveBotFlowStep == CreateServiceNameStep)
        {
            context.ServiceName = text.Trim();
            await SetFlowAsync(user, CreateServiceFlow, CreateServiceDescriptionStep, context, cancellationToken);
            return new BotScreen(
                $"""
                <b>➕ Новый сервис</b>
                Название: <b>{Encode(context.ServiceName ?? string.Empty)}</b>

                Теперь введите описание сервиса.
                """,
                TemplateParseMode.Html,
                BuildBackCancelMarkup());
        }

        if (user.ActiveBotFlowStep == CreateServiceDescriptionStep)
        {
            context.ServiceDescription = text.Trim();
            await SetFlowAsync(user, CreateServiceFlow, CreateServiceConfirmStep, context, cancellationToken);
            return new BotScreen(
                $"""
                <b>➕ Подтверждение создания сервиса</b>
                Название: <b>{Encode(context.ServiceName ?? string.Empty)}</b>
                Описание:
                {Encode(context.ServiceDescription ?? string.Empty)}
                """,
                TemplateParseMode.Html,
                new InlineKeyboardMarkup(
                [
                    [InlineKeyboardButton.WithCallbackData("✅ Создать", BotCallbackKeys.FlowCreateServiceConfirm)],
                    [InlineKeyboardButton.WithCallbackData("⬅️ Назад", BotCallbackKeys.FlowBack), InlineKeyboardButton.WithCallbackData("✖️ Отмена", BotCallbackKeys.FlowCancel)]
                ]));
        }

        return BuildPromptScreen("Текущее действие не ожидает текстовый ввод.", BuildCancelMarkup());
    }

    private async Task<BotScreen> HandleCreateTemplateMessageAsync(AppUser user, ConversationContext context, string text, CancellationToken cancellationToken)
    {
        if (user.ActiveBotFlowStep == CreateTemplateKeyStep)
        {
            context.TemplateKey = text.Trim();
            await SetFlowAsync(user, CreateTemplateFlow, CreateTemplateBodyStep, context, cancellationToken);
            return BuildPromptScreen(
                "<b>🧩 Новый шаблон</b>\nВведите тело шаблона одним сообщением.",
                BuildBackCancelMarkup());
        }

        if (user.ActiveBotFlowStep == CreateTemplateBodyStep)
        {
            var result = await administrationService.UpsertTemplateAsync(
                user.TelegramUserId,
                context.ServicePublicId!,
                context.TemplateKey ?? string.Empty,
                context.TemplateParseMode ?? TemplateParseMode.PlainText,
                text,
                cancellationToken);

            ClearFlow(user);
            await dbContext.SaveChangesAsync(cancellationToken);

            return new BotScreen(
                $"""
                <b>✅ Шаблон сохранен</b>
                Сервис: <code>{Encode(result.ServicePublicId)}</code>
                Шаблон: <code>{Encode(result.TemplateKey)}</code>
                """,
                TemplateParseMode.Html,
                BuildServiceNavigationMarkup(result.ServicePublicId));
        }

        return BuildPromptScreen("Сначала выберите parse mode кнопкой.", BuildCancelMarkup());
    }

    private async Task<BotScreen> HandlePersonalInviteMessageAsync(AppUser user, ConversationContext context, string text, CancellationToken cancellationToken)
    {
        if (user.ActiveBotFlowStep == PersonalInviteExternalKeyStep)
        {
            context.ExternalUserKey = text.Trim();
            await SetFlowAsync(user, PersonalInviteFlow, PersonalInviteExpiryChoiceStep, context, cancellationToken);
            return new BotScreen(
                "<b>👤 Персональное приглашение</b>\nВыберите срок действия приглашения.",
                TemplateParseMode.Html,
                new InlineKeyboardMarkup(
                [
                    [InlineKeyboardButton.WithCallbackData("Без срока", BotCallbackKeys.FlowPersonalInviteExpiryNone)],
                    [InlineKeyboardButton.WithCallbackData("Ввести часы вручную", BotCallbackKeys.FlowPersonalInviteExpiryManual)],
                    [InlineKeyboardButton.WithCallbackData("⬅️ Назад", BotCallbackKeys.FlowBack), InlineKeyboardButton.WithCallbackData("✖️ Отмена", BotCallbackKeys.FlowCancel)]
                ]));
        }

        if (user.ActiveBotFlowStep == PersonalInviteExpiryManualStep)
        {
            if (!int.TryParse(text.Trim(), out var expiresInHours) || expiresInHours <= 0)
            {
                return BuildPromptScreen(
                    "<b>👤 Персональное приглашение</b>\nВведите положительное число часов.",
                    BuildBackCancelMarkup());
            }

            var result = await administrationService.CreateInviteAsync(
                user.TelegramUserId,
                context.ServicePublicId!,
                InviteCodeType.Personal,
                context.ExternalUserKey,
                1,
                expiresInHours,
                cancellationToken);

            ClearFlow(user);
            await dbContext.SaveChangesAsync(cancellationToken);
            return BuildInviteCreatedScreen(result, "<b>✅ Персональное приглашение создано</b>");
        }

        return BuildPromptScreen("Текущее действие не ожидает текстовый ввод.", BuildCancelMarkup());
    }

    private async Task<BotScreen> HandleAddAdminMessageAsync(AppUser user, ConversationContext context, string text, CancellationToken cancellationToken)
    {
        if (!long.TryParse(text.Trim(), out var targetUserId))
        {
            return BuildPromptScreen(
                "<b>👥 Добавление администратора</b>\nВведите корректный числовой Telegram user id.",
                BuildCancelMarkup());
        }

        await administrationService.AddServiceAdminAsync(user.TelegramUserId, context.ServicePublicId!, targetUserId, cancellationToken);
        ClearFlow(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new BotScreen(
            $"""
            <b>✅ Администратор добавлен</b>
            Сервис: <code>{Encode(context.ServicePublicId!)}</code>
            Пользователь: <code>{targetUserId}</code>
            """,
            TemplateParseMode.Html,
            BuildServiceNavigationMarkup(context.ServicePublicId!));
    }

    private async Task<BotScreen> HandleRemoveAdminMessageAsync(AppUser user, ConversationContext context, string text, CancellationToken cancellationToken)
    {
        if (user.ActiveBotFlowStep != RemoveAdminUserIdStep)
        {
            return BuildPromptScreen("Выберите администратора кнопкой или включите ручной ввод.", BuildCancelMarkup());
        }

        if (!long.TryParse(text.Trim(), out var targetUserId))
        {
            return BuildPromptScreen(
                "<b>👥 Удаление администратора</b>\nВведите корректный числовой Telegram user id.",
                BuildBackCancelMarkup());
        }

        await administrationService.RemoveServiceAdminAsync(user.TelegramUserId, context.ServicePublicId!, targetUserId, cancellationToken);
        ClearFlow(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new BotScreen(
            $"""
            <b>✅ Администратор удален</b>
            Сервис: <code>{Encode(context.ServicePublicId!)}</code>
            Пользователь: <code>{targetUserId}</code>
            """,
            TemplateParseMode.Html,
            BuildServiceNavigationMarkup(context.ServicePublicId!));
    }

    private async Task<BotScreen> HandleCreatorPermissionMessageAsync(AppUser user, ConversationContext context, string text, CancellationToken cancellationToken)
    {
        if (!long.TryParse(text.Trim(), out var targetUserId))
        {
            return BuildPromptScreen(
                "<b>👑 Управление создателями</b>\nВведите корректный числовой Telegram user id.",
                BuildCancelMarkup(BotCallbackKeys.CreatorManagement));
        }

        var allowed = context.CreatorPermissionAllowed == true;
        var success = await administrationService.SetCreatorPermissionAsync(user.TelegramUserId, targetUserId, allowed, cancellationToken);
        ClearFlow(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new BotScreen(
            success
                ? allowed
                    ? $"<b>✅ Доступ выдан</b>\nПользователь <code>{targetUserId}</code> теперь может создавать сервисы."
                    : $"<b>✅ Доступ отозван</b>\nПользователь <code>{targetUserId}</code> больше не может создавать сервисы."
                : "<b>⛔ Доступ запрещен</b>\nТолько супер-админ может управлять создателями сервисов.",
            TemplateParseMode.Html,
            BuildCreatorManagementMarkup());
    }

    private async Task<BotScreen> ConfirmCreateServiceAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(telegramUserId, cancellationToken);
        if (user.ActiveBotFlowType != CreateServiceFlow || user.ActiveBotFlowStep != CreateServiceConfirmStep)
        {
            return BuildPromptScreen("Сначала завершите ввод данных сервиса.", BuildCancelMarkup());
        }

        var context = DeserializeContext(user.ActiveBotFlowContextJson);
        var result = await administrationService.CreateServiceAsync(
            telegramUserId,
            context.ServiceName ?? string.Empty,
            context.ServiceDescription ?? string.Empty,
            cancellationToken);

        ClearFlow(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new BotScreen(
            $"""
            <b>✅ Сервис создан</b>
            Идентификатор сервиса: <code>{Encode(result.PublicId)}</code>
            Ключ сервиса: <code>{Encode(result.ServiceKey)}</code>

            <b>⚠️ Сохраните ключ сервиса прямо сейчас.</b>
            <b>Позже получить этот ключ повторно будет нельзя.</b>
            """,
            TemplateParseMode.Html,
            BuildServiceActionsMarkup(result.PublicId));
    }

    private async Task<BotScreen> SelectTemplateParseModeAsync(long telegramUserId, string parseModeValue, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(telegramUserId, cancellationToken);
        if (user.ActiveBotFlowType != CreateTemplateFlow)
        {
            return BuildPromptScreen("Сначала запустите создание шаблона.", BuildCancelMarkup());
        }

        if (!Enum.TryParse<TemplateParseMode>(parseModeValue, true, out var parseMode))
        {
            return BuildPromptScreen("Выберите корректный parse mode.", BuildCancelMarkup());
        }

        var context = DeserializeContext(user.ActiveBotFlowContextJson);
        context.TemplateParseMode = parseMode;
        await SetFlowAsync(user, CreateTemplateFlow, CreateTemplateKeyStep, context, cancellationToken);
        return BuildPromptScreen(
            "<b>🧩 Новый шаблон</b>\nВведите `templateKey` одним сообщением.",
            BuildBackCancelMarkup());
    }

    private async Task<BotScreen> FinalizePersonalInviteWithoutExpiryAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(telegramUserId, cancellationToken);
        if (user.ActiveBotFlowType != PersonalInviteFlow)
        {
            return BuildPromptScreen("Сначала запустите создание приглашения.", BuildCancelMarkup());
        }

        var context = DeserializeContext(user.ActiveBotFlowContextJson);
        var result = await administrationService.CreateInviteAsync(
            telegramUserId,
            context.ServicePublicId!,
            InviteCodeType.Personal,
            context.ExternalUserKey,
            1,
            null,
            cancellationToken);

        ClearFlow(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return BuildInviteCreatedScreen(result, "<b>✅ Персональное приглашение создано</b>");
    }

    private async Task<BotScreen> SwitchPersonalInviteToManualExpiryAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(telegramUserId, cancellationToken);
        if (user.ActiveBotFlowType != PersonalInviteFlow)
        {
            return BuildPromptScreen("Сначала запустите создание приглашения.", BuildCancelMarkup());
        }

        var context = DeserializeContext(user.ActiveBotFlowContextJson);
        await SetFlowAsync(user, PersonalInviteFlow, PersonalInviteExpiryManualStep, context, cancellationToken);
        return BuildPromptScreen(
            "<b>👤 Персональное приглашение</b>\nВведите срок действия в часах.",
            BuildBackCancelMarkup());
    }

    private async Task<BotScreen> SwitchRemoveAdminToManualEntryAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(telegramUserId, cancellationToken);
        if (user.ActiveBotFlowType != RemoveAdminFlow)
        {
            return BuildPromptScreen("Сначала откройте сценарий удаления администратора.", BuildCancelMarkup());
        }

        var context = DeserializeContext(user.ActiveBotFlowContextJson);
        await SetFlowAsync(user, RemoveAdminFlow, RemoveAdminUserIdStep, context, cancellationToken);
        return BuildPromptScreen(
            "<b>👥 Удаление администратора</b>\nВведите Telegram user id пользователя.",
            BuildBackCancelMarkup());
    }

    private async Task<BotScreen> RemoveSelectedAdminAsync(long telegramUserId, string userIdValue, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(telegramUserId, cancellationToken);
        if (user.ActiveBotFlowType != RemoveAdminFlow)
        {
            return BuildPromptScreen("Сначала откройте сценарий удаления администратора.", BuildCancelMarkup());
        }

        if (!long.TryParse(userIdValue, out var targetUserId))
        {
            return BuildPromptScreen("Не удалось определить администратора для удаления.", BuildCancelMarkup());
        }

        var context = DeserializeContext(user.ActiveBotFlowContextJson);
        await administrationService.RemoveServiceAdminAsync(telegramUserId, context.ServicePublicId!, targetUserId, cancellationToken);
        ClearFlow(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new BotScreen(
            $"""
            <b>✅ Администратор удален</b>
            Сервис: <code>{Encode(context.ServicePublicId!)}</code>
            Пользователь: <code>{targetUserId}</code>
            """,
            TemplateParseMode.Html,
            BuildServiceNavigationMarkup(context.ServicePublicId!));
    }

    private async Task<BotScreen> GoBackAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(telegramUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(user.ActiveBotFlowType) || string.IsNullOrWhiteSpace(user.ActiveBotFlowStep))
        {
            return BuildPromptScreen("Нет активного действия для возврата.", BuildCancelMarkup());
        }

        var context = DeserializeContext(user.ActiveBotFlowContextJson);
        return (user.ActiveBotFlowType, user.ActiveBotFlowStep) switch
        {
            (CreateServiceFlow, CreateServiceDescriptionStep) => await BackToStepAsync(user, CreateServiceFlow, CreateServiceNameStep, context, "<b>➕ Новый сервис</b>\nВведите название сервиса.", cancellationToken),
            (CreateServiceFlow, CreateServiceConfirmStep) => await BackToCreateServiceDescriptionAsync(user, context, cancellationToken),
            (CreateTemplateFlow, CreateTemplateKeyStep) => await BackToParseModeAsync(user, context, cancellationToken),
            (CreateTemplateFlow, CreateTemplateBodyStep) => await BackToStepAsync(user, CreateTemplateFlow, CreateTemplateKeyStep, context, "<b>🧩 Новый шаблон</b>\nВведите `templateKey` одним сообщением.", cancellationToken),
            (PersonalInviteFlow, PersonalInviteExpiryChoiceStep) => await BackToStepAsync(user, PersonalInviteFlow, PersonalInviteExternalKeyStep, context, "<b>👤 Персональное приглашение</b>\nВведите `externalUserKey` одним сообщением.", cancellationToken),
            (PersonalInviteFlow, PersonalInviteExpiryManualStep) => await BackToPersonalInviteExpiryChoiceAsync(user, context, cancellationToken),
            (RemoveAdminFlow, RemoveAdminUserIdStep) => await BackToRemoveAdminSelectionAsync(user, context, cancellationToken),
            _ => await CancelFlowAsync(telegramUserId, cancellationToken)
        };
    }

    private async Task<BotScreen> CancelFlowAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        await ClearAsync(telegramUserId, cancellationToken);
        return new BotScreen(
            "<b>👌 Действие отменено</b>\nВыберите следующий раздел кнопками ниже.",
            TemplateParseMode.Html,
            new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithCallbackData("🏠 Главное меню", BotCallbackKeys.Home) } }));
    }

    private async Task<BotScreen> BackToStepAsync(AppUser user, string flowType, string step, ConversationContext context, string prompt, CancellationToken cancellationToken)
    {
        await SetFlowAsync(user, flowType, step, context, cancellationToken);
        return BuildPromptScreen(prompt, BuildBackCancelMarkup());
    }

    private async Task<BotScreen> BackToParseModeAsync(AppUser user, ConversationContext context, CancellationToken cancellationToken)
    {
        await SetFlowAsync(user, CreateTemplateFlow, CreateTemplateParseModeStep, context, cancellationToken);
        return new BotScreen(
            "<b>🧩 Новый шаблон</b>\nВыберите parse mode.",
            TemplateParseMode.Html,
            BuildParseModeMarkup());
    }

    private async Task<BotScreen> BackToCreateServiceDescriptionAsync(AppUser user, ConversationContext context, CancellationToken cancellationToken)
    {
        await SetFlowAsync(user, CreateServiceFlow, CreateServiceDescriptionStep, context, cancellationToken);
        return new BotScreen(
            $"""
            <b>➕ Новый сервис</b>
            Название: <b>{Encode(context.ServiceName ?? string.Empty)}</b>

            Введите описание сервиса.
            """,
            TemplateParseMode.Html,
            BuildBackCancelMarkup());
    }

    private async Task<BotScreen> BackToPersonalInviteExpiryChoiceAsync(AppUser user, ConversationContext context, CancellationToken cancellationToken)
    {
        await SetFlowAsync(user, PersonalInviteFlow, PersonalInviteExpiryChoiceStep, context, cancellationToken);
        return new BotScreen(
            "<b>👤 Персональное приглашение</b>\nВыберите срок действия приглашения.",
            TemplateParseMode.Html,
            new InlineKeyboardMarkup(
            [
                [InlineKeyboardButton.WithCallbackData("Без срока", BotCallbackKeys.FlowPersonalInviteExpiryNone)],
                [InlineKeyboardButton.WithCallbackData("Ввести часы вручную", BotCallbackKeys.FlowPersonalInviteExpiryManual)],
                [InlineKeyboardButton.WithCallbackData("⬅️ Назад", BotCallbackKeys.FlowBack), InlineKeyboardButton.WithCallbackData("✖️ Отмена", BotCallbackKeys.FlowCancel)]
            ]));
    }

    private async Task<BotScreen> BackToRemoveAdminSelectionAsync(AppUser user, ConversationContext context, CancellationToken cancellationToken)
    {
        await SetFlowAsync(user, RemoveAdminFlow, RemoveAdminSelectStep, context, cancellationToken);
        var admins = await administrationService.GetServiceAdminsAsync(user.TelegramUserId, context.ServicePublicId!, cancellationToken);
        return new BotScreen(
            "<b>👥 Удаление администратора</b>\nВыберите пользователя из списка или перейдите к ручному вводу.",
            TemplateParseMode.Html,
            BuildRemoveAdminMarkup(context.ServicePublicId!, admins));
    }

    private static BotScreen BuildPromptScreen(string message, InlineKeyboardMarkup replyMarkup) =>
        new(message, TemplateParseMode.Html, replyMarkup);

    private BotScreen BuildInviteCreatedScreen(CreateInviteResult result, string title)
    {
        return new BotScreen(
            $"""
            {title}
            Сервис: <code>{Encode(result.ServicePublicId)}</code>
            Код: <code>{Encode(result.InviteCode)}</code>
            """,
            TemplateParseMode.Html,
            BuildServiceNavigationMarkup(result.ServicePublicId));
    }

    private static InlineKeyboardMarkup BuildParseModeMarkup() => new(
    [
        [InlineKeyboardButton.WithCallbackData("PlainText", $"{BotCallbackKeys.FlowTemplateParseModePrefix}{TemplateParseMode.PlainText}")],
        [InlineKeyboardButton.WithCallbackData("Html", $"{BotCallbackKeys.FlowTemplateParseModePrefix}{TemplateParseMode.Html}"), InlineKeyboardButton.WithCallbackData("Markdown", $"{BotCallbackKeys.FlowTemplateParseModePrefix}{TemplateParseMode.Markdown}")],
        [InlineKeyboardButton.WithCallbackData("MarkdownV2", $"{BotCallbackKeys.FlowTemplateParseModePrefix}{TemplateParseMode.MarkdownV2}")],
        [InlineKeyboardButton.WithCallbackData("✖️ Отмена", BotCallbackKeys.FlowCancel)]
    ]);

    private static InlineKeyboardMarkup BuildCancelMarkup(string? fallbackCallback = null) => new(
    [
        fallbackCallback is null
            ? [InlineKeyboardButton.WithCallbackData("✖️ Отмена", BotCallbackKeys.FlowCancel)]
            : [InlineKeyboardButton.WithCallbackData("⬅️ Назад", fallbackCallback), InlineKeyboardButton.WithCallbackData("✖️ Отмена", BotCallbackKeys.FlowCancel)]
    ]);

    private static InlineKeyboardMarkup BuildBackCancelMarkup() => new(
    [
        [InlineKeyboardButton.WithCallbackData("⬅️ Назад", BotCallbackKeys.FlowBack), InlineKeyboardButton.WithCallbackData("✖️ Отмена", BotCallbackKeys.FlowCancel)]
    ]);

    private static InlineKeyboardMarkup BuildServiceNavigationMarkup(string servicePublicId) => new(
    [
        [InlineKeyboardButton.WithCallbackData("⬅️ К сервису", $"{BotCallbackKeys.ServiceViewPrefix}{servicePublicId}"), InlineKeyboardButton.WithCallbackData("🏠 Главное меню", BotCallbackKeys.Home)]
    ]);

    private static InlineKeyboardMarkup BuildServiceActionsMarkup(string servicePublicId) => new(
    [
        [InlineKeyboardButton.WithCallbackData("🧩 Создать шаблон", $"{BotCallbackKeys.FlowStartCreateTemplatePrefix}{servicePublicId}"), InlineKeyboardButton.WithCallbackData("🎟️ Общее приглашение", $"{BotCallbackKeys.ServiceGeneralInvitePrefix}{servicePublicId}")],
        [InlineKeyboardButton.WithCallbackData("👤 Персональное приглашение", $"{BotCallbackKeys.FlowStartPersonalInvitePrefix}{servicePublicId}"), InlineKeyboardButton.WithCallbackData("👥 Показать админов", $"{BotCallbackKeys.ServiceAdminsPrefix}{servicePublicId}")],
        [InlineKeyboardButton.WithCallbackData("➕ Добавить админа", $"{BotCallbackKeys.FlowStartAddAdminPrefix}{servicePublicId}"), InlineKeyboardButton.WithCallbackData("➖ Удалить админа", $"{BotCallbackKeys.FlowStartRemoveAdminPrefix}{servicePublicId}")],
        [InlineKeyboardButton.WithCallbackData("⬅️ К сервисам", BotCallbackKeys.Services), InlineKeyboardButton.WithCallbackData("🏠 Главное меню", BotCallbackKeys.Home)]
    ]);

    private static InlineKeyboardMarkup BuildRemoveAdminMarkup(string servicePublicId, IReadOnlyCollection<long> admins)
    {
        var rows = admins.Select(x => new[] { InlineKeyboardButton.WithCallbackData($"➖ {x}", $"{BotCallbackKeys.FlowRemoveAdminSelectPrefix}{x}") }).ToList();
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("⌨️ Ввести ID вручную", BotCallbackKeys.FlowRemoveAdminManual) });
        rows.Add(new[] { InlineKeyboardButton.WithCallbackData("⬅️ К сервису", $"{BotCallbackKeys.ServiceViewPrefix}{servicePublicId}"), InlineKeyboardButton.WithCallbackData("✖️ Отмена", BotCallbackKeys.FlowCancel) });
        return new InlineKeyboardMarkup(rows);
    }

    private static InlineKeyboardMarkup BuildCreatorManagementMarkup() => new(
    [
        [InlineKeyboardButton.WithCallbackData("✅ Разрешить создание", BotCallbackKeys.FlowStartCreatorAllow), InlineKeyboardButton.WithCallbackData("⛔ Запретить создание", BotCallbackKeys.FlowStartCreatorDeny)],
        [InlineKeyboardButton.WithCallbackData("🏠 Главное меню", BotCallbackKeys.Home)]
    ]);

    private async Task<AppUser> GetUserAsync(long telegramUserId, CancellationToken cancellationToken) =>
        await dbContext.Users.FindAsync([telegramUserId], cancellationToken)
        ?? throw new InvalidOperationException("Пользователь не найден.");

    private async Task SetFlowAsync(long telegramUserId, string flowType, string step, ConversationContext context, CancellationToken cancellationToken)
    {
        var user = await GetUserAsync(telegramUserId, cancellationToken);
        await SetFlowAsync(user, flowType, step, context, cancellationToken);
    }

    private async Task SetFlowAsync(AppUser user, string flowType, string step, ConversationContext context, CancellationToken cancellationToken)
    {
        user.ActiveBotFlowType = flowType;
        user.ActiveBotFlowStep = step;
        user.ActiveBotFlowContextJson = JsonSerializer.Serialize(context);
        user.ActiveBotFlowStartedAtUtc ??= timeProvider.UtcNow;
        user.ActiveBotFlowUpdatedAtUtc = timeProvider.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ClearFlow(AppUser user)
    {
        user.ActiveBotFlowType = null;
        user.ActiveBotFlowStep = null;
        user.ActiveBotFlowContextJson = null;
        user.ActiveBotFlowStartedAtUtc = null;
        user.ActiveBotFlowUpdatedAtUtc = null;
    }

    private static ConversationContext DeserializeContext(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new ConversationContext()
            : JsonSerializer.Deserialize<ConversationContext>(json) ?? new ConversationContext();

    private static string Encode(string value) => System.Net.WebUtility.HtmlEncode(value);

    private sealed class ConversationContext
    {
        public string? ServicePublicId { get; set; }

        public string? ServiceName { get; set; }

        public string? ServiceDescription { get; set; }

        public TemplateParseMode? TemplateParseMode { get; set; }

        public string? TemplateKey { get; set; }

        public string? ExternalUserKey { get; set; }

        public bool? CreatorPermissionAllowed { get; set; }
    }
}
