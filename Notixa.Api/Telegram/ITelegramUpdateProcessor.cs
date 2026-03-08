using Telegram.Bot.Types;

namespace Notixa.Api.Telegram;

public interface ITelegramUpdateProcessor
{
    Task ProcessAsync(Update update, CancellationToken cancellationToken);
}
