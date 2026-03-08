using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Notixa.Api.Domain.Enums;
using Notixa.Api.Telegram;

namespace Notixa.Api.Services;

public sealed class TelegramMessageSender(IBotClientAccessor botClientAccessor, ILogger<TelegramMessageSender> logger) : ITelegramMessageSender
{
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
        if (parseMode == TemplateParseMode.PlainText)
        {
            await client.SendMessage(
                chatId: telegramUserId,
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

        await client.SendMessage(
            chatId: telegramUserId,
            text: text,
            parseMode: telegramParseMode,
            replyMarkup: replyMarkup,
            cancellationToken: cancellationToken);
    }
}
