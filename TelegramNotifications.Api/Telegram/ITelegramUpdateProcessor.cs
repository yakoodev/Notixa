using Telegram.Bot.Types;

namespace TelegramNotifications.Api.Telegram;

public interface ITelegramUpdateProcessor
{
    Task ProcessAsync(Update update, CancellationToken cancellationToken);
}
