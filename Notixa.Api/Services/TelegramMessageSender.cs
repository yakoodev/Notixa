using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using Notixa.Api.Domain.Enums;
using Notixa.Api.Telegram;

namespace Notixa.Api.Services;

public sealed class TelegramMessageSender(IBotClientAccessor botClientAccessor, ILogger<TelegramMessageSender> logger) : ITelegramMessageSender
{
    public async Task<TelegramScreenResult> SendOrEditScreenAsync(
        long chatId,
        int? messageId,
        string text,
        TemplateParseMode parseMode,
        CancellationToken cancellationToken,
        ReplyMarkup? replyMarkup = null)
    {
        if (!botClientAccessor.IsConfigured)
        {
            logger.LogWarning("Telegram bot is not configured. Skipping screen update to chat {ChatId}.", chatId);
            return new TelegramScreenResult(chatId, messageId ?? 0, false);
        }

        var client = botClientAccessor.Client!;
        if (messageId.HasValue)
        {
            try
            {
                await EditMessageAsync(client, chatId, messageId.Value, text, parseMode, replyMarkup as InlineKeyboardMarkup, cancellationToken);
                return new TelegramScreenResult(chatId, messageId.Value, true);
            }
            catch (RequestException ex) when (IsMessageNotModified(ex))
            {
                return new TelegramScreenResult(chatId, messageId.Value, true);
            }
            catch (RequestException ex) when (CanFallbackToSend(ex))
            {
                logger.LogDebug(ex, "Falling back to sending a new bot screen for chat {ChatId}.", chatId);
            }
        }

        var sent = await SendMessageCoreAsync(client, chatId, text, parseMode, replyMarkup, cancellationToken);
        return new TelegramScreenResult(chatId, sent.MessageId, false);
    }

    public async Task SendAsync(
        long telegramUserId,
        string text,
        TemplateParseMode parseMode,
        CancellationToken cancellationToken,
        global::Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? replyMarkup = null)
    {
        if (!botClientAccessor.IsConfigured)
        {
            logger.LogWarning("Telegram bot is not configured. Skipping send to {TelegramUserId}.", telegramUserId);
            return;
        }

        var client = botClientAccessor.Client!;
        await SendMessageCoreAsync(client, telegramUserId, text, parseMode, replyMarkup, cancellationToken);
    }

    private static async Task<global::Telegram.Bot.Types.Message> SendMessageCoreAsync(
        ITelegramBotClient client,
        long chatId,
        string text,
        TemplateParseMode parseMode,
        ReplyMarkup? replyMarkup,
        CancellationToken cancellationToken)
    {
        if (parseMode == TemplateParseMode.PlainText)
        {
            return await client.SendMessage(
                chatId: chatId,
                text: text,
                replyMarkup: replyMarkup,
                cancellationToken: cancellationToken);
        }

        var telegramParseMode = parseMode switch
        {
            TemplateParseMode.Markdown => ParseMode.Markdown,
            TemplateParseMode.MarkdownV2 => ParseMode.MarkdownV2,
            TemplateParseMode.Html => ParseMode.Html,
            _ => ParseMode.None
        };

        return await client.SendMessage(
            chatId: chatId,
            text: text,
            parseMode: telegramParseMode,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
    }

    private static async Task EditMessageAsync(
        ITelegramBotClient client,
        long chatId,
        int messageId,
        string text,
        TemplateParseMode parseMode,
        InlineKeyboardMarkup? replyMarkup,
        CancellationToken cancellationToken)
    {
        if (parseMode == TemplateParseMode.PlainText)
        {
            await client.EditMessageText(
                chatId: chatId,
                messageId: messageId,
                text: text,
                replyMarkup: replyMarkup,
                cancellationToken: cancellationToken);
            return;
        }

        var telegramParseMode = parseMode switch
        {
            TemplateParseMode.Markdown => ParseMode.Markdown,
            TemplateParseMode.MarkdownV2 => ParseMode.MarkdownV2,
            TemplateParseMode.Html => ParseMode.Html,
            _ => ParseMode.None
        };

        await client.EditMessageText(
            chatId: chatId,
            messageId: messageId,
            text: text,
            parseMode: telegramParseMode,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
    }

    private static bool IsMessageNotModified(RequestException ex) =>
        ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase);

    private static bool CanFallbackToSend(RequestException ex) =>
        ex.Message.Contains("message to edit not found", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("message can't be edited", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("message identifier is not specified", StringComparison.OrdinalIgnoreCase);
}
