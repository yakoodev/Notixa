using Telegram.Bot;

namespace TelegramNotifications.Api.Telegram;

public interface IBotClientAccessor
{
    bool IsConfigured { get; }

    ITelegramBotClient? Client { get; }
}
