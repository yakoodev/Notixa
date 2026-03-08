using Microsoft.Extensions.Options;
using Telegram.Bot;
using TelegramNotifications.Api.Options;

namespace TelegramNotifications.Api.Telegram;

public sealed class BotClientAccessor : IBotClientAccessor
{
    private readonly Lazy<ITelegramBotClient?> _lazyClient;

    public BotClientAccessor(IOptions<TelegramBotOptions> options)
    {
        _lazyClient = new Lazy<ITelegramBotClient?>(() =>
        {
            if (string.IsNullOrWhiteSpace(options.Value.BotToken))
            {
                return null;
            }

            return new TelegramBotClient(options.Value.BotToken.Trim());
        });
    }

    public bool IsConfigured => _lazyClient.Value is not null;

    public ITelegramBotClient? Client => _lazyClient.Value;
}
