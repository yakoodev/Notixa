using Notixa.Api.Domain.Enums;

namespace Notixa.Api.Services;

public sealed record PlatformCommandResult(IReadOnlyCollection<PlatformReply> Replies)
{
    public static PlatformCommandResult Single(
        string message,
        TemplateParseMode parseMode = TemplateParseMode.PlainText,
        global::Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? replyMarkup = null)
        => new([new PlatformReply(message, parseMode, replyMarkup)]);
}

public sealed record PlatformReply(
    string Message,
    TemplateParseMode ParseMode,
    global::Telegram.Bot.Types.ReplyMarkups.ReplyMarkup? ReplyMarkup = null);
