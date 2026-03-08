namespace Notixa.Api.Telegram;

internal static class TelegramPollingLogPolicy
{
    internal const int WarningThreshold = 3;

    public static bool ShouldEscalateWarning(int transientFailureCount) =>
        transientFailureCount == WarningThreshold;

    public static bool ShouldLogRecovery(int transientFailureCount) =>
        transientFailureCount >= WarningThreshold;
}
