using Notixa.Api.Domain.Enums;
using Notixa.Api.Services;

namespace Notixa.Tests.TestDoubles;

public sealed class FakeTelegramMessageSender : ITelegramMessageSender
{
    public List<(long UserId, string Text, TemplateParseMode ParseMode, global::Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? ReplyMarkup)> SentMessages { get; } = [];
    public List<(long ChatId, int MessageId, bool WasEdited, string Text, TemplateParseMode ParseMode, global::Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? ReplyMarkup)> ScreenMessages { get; } = [];
    private int _nextMessageId = 100;

    public Task<TelegramScreenResult> SendOrEditScreenAsync(
        long chatId,
        int? messageId,
        string text,
        TemplateParseMode parseMode,
        CancellationToken cancellationToken,
        global::Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? replyMarkup = null)
    {
        var resolvedMessageId = messageId ?? _nextMessageId++;
        ScreenMessages.Add((chatId, resolvedMessageId, messageId.HasValue, text, parseMode, replyMarkup));
        return Task.FromResult(new TelegramScreenResult(chatId, resolvedMessageId, messageId.HasValue));
    }

    public Task SendAsync(
        long telegramUserId,
        string text,
        TemplateParseMode parseMode,
        CancellationToken cancellationToken,
        global::Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? replyMarkup = null)
    {
        SentMessages.Add((telegramUserId, text, parseMode, replyMarkup));
        return Task.CompletedTask;
    }
}
