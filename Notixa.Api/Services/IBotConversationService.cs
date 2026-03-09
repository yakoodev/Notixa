using Notixa.Api.Telegram;

namespace Notixa.Api.Services;

public interface IBotConversationService
{
    Task<BotScreen?> HandleMessageAsync(long telegramUserId, string text, CancellationToken cancellationToken);

    Task<BotScreen?> HandleCallbackAsync(long telegramUserId, string data, CancellationToken cancellationToken);

    Task ClearAsync(long telegramUserId, CancellationToken cancellationToken);
}
