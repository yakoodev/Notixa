using Telegram.Bot;

namespace Notixa.Api.Telegram;

public interface IBotClientAccessor
{
    bool IsConfigured { get; }

    ITelegramBotClient? Client { get; }
}
