using Notixa.Api.Domain.Enums;

namespace Notixa.Api.Services;

public interface ITelegramMessageSender
{
    Task SendAsync(
        long telegramUserId,
        string text,
        TemplateParseMode parseMode,
        CancellationToken cancellationToken,
        global::Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? replyMarkup = null);
}
