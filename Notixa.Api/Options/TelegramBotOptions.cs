namespace Notixa.Api.Options;

public sealed class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    public string BotToken { get; set; } = string.Empty;

    public string BotUsername { get; set; } = string.Empty;

    public string UpdateMode { get; set; } = TelegramUpdateModes.LongPolling;

    public string WebhookBaseUrl { get; set; } = string.Empty;
}

public static class TelegramUpdateModes
{
    public const string LongPolling = "LongPolling";
    public const string Webhook = "Webhook";
}
