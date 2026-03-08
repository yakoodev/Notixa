using Notixa.Api.Domain.Enums;
using Notixa.Api.Services;

namespace Notixa.Tests.TestDoubles;

public sealed class FakeTelegramMessageSender : ITelegramMessageSender
{
    public List<(long UserId, string Text, TemplateParseMode ParseMode, global::Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? ReplyMarkup)> SentMessages { get; } = [];

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
