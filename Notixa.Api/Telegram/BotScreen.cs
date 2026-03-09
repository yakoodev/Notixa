using Notixa.Api.Domain.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Notixa.Api.Telegram;

public sealed record BotScreen(string Message, TemplateParseMode ParseMode, ReplyMarkup? ReplyMarkup = null);
