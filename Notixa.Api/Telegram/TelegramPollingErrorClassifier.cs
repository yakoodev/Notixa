using System.Net.Http;
using Telegram.Bot.Exceptions;

namespace Notixa.Api.Telegram;

internal static class TelegramPollingErrorClassifier
{
    public static bool IsTransient(Exception exception) =>
        exception switch
        {
            RequestException { InnerException: HttpRequestException } => true,
            HttpRequestException => true,
            _ => false
        };
}
