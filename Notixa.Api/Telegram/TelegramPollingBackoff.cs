namespace Notixa.Api.Telegram;

internal static class TelegramPollingBackoff
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    public static TimeSpan GetDelay(int transientFailureCount)
    {
        if (transientFailureCount <= 1)
        {
            return Delays[0];
        }

        return Delays[Math.Min(transientFailureCount - 1, Delays.Length - 1)];
    }
}
