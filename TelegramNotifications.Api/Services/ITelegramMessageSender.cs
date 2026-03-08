using TelegramNotifications.Api.Domain.Enums;

namespace TelegramNotifications.Api.Services;

public interface ITelegramMessageSender
{
    Task SendAsync(
        long telegramUserId,
        string text,
        TemplateParseMode parseMode,
        CancellationToken cancellationToken,
        global::Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? replyMarkup = null);
}
