using System.Net;
using Telegram.Bot;

namespace Notixa.Api.Telegram;

internal static class TelegramBotClientFactory
{
    internal static readonly TimeSpan PollingRequestTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan HttpClientTimeout = TimeSpan.FromSeconds(90);

    public static ITelegramBotClient Create(string token) =>
        new TelegramBotClient(token, CreateHttpClient());

    internal static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = Timeout.InfiniteTimeSpan,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(10),
            UseCookies = false
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = HttpClientTimeout,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
    }
}
