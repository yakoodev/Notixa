using Notixa.Api.Telegram;
using Telegram.Bot;

namespace Notixa.Tests.TestDoubles;

public sealed class FakeBotClientAccessor : IBotClientAccessor
{
    public bool IsConfigured => false;

    public ITelegramBotClient? Client => null;
}
